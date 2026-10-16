#pragma once
#include "FunkePhys/AABB.hpp"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
#include <vector>

namespace FunkePhys::Detail {

    // Uniform grid rebuilt from scratch each time it is used. Proxies are bucketed by
    // the cells their AABB covers, the (cell, proxy) entries are radix-sorted by cell,
    // and runs of equal cells are walked. Every buffer is reused, so a rebuild does not
    // allocate once the grid has warmed up.
    class SpatialGrid
    {
    public:
        // A proxy covering more cells than this is tested against everything instead.
        static constexpr std::uint32_t k_MaxCellsPerProxy = 64;

        void Build(const AABB* boxes, std::uint32_t count, float cellSize)
        {
            m_Count = count;
            m_CellSize = cellSize;
            m_InvCell = 1.0f / cellSize;
            m_Boxes.assign(boxes, boxes + count);
            m_Entries.clear();
            m_Large.clear();
            m_Flags.assign(count, 0);

            bool first = true;
            for (std::uint32_t i = 0; i < count; ++i) {
                const AABB& box = m_Boxes[i];
                if (!std::isfinite(box.min.x) || !std::isfinite(box.min.y)
                    || !std::isfinite(box.max.x) || !std::isfinite(box.max.y)) {
                    continue;
                }
                m_Flags[i] = k_Valid;
                if (first) {
                    m_Bounds = box;
                    first = false;
                }
                else {
                    m_Bounds.min.x = std::min(m_Bounds.min.x, box.min.x);
                    m_Bounds.min.y = std::min(m_Bounds.min.y, box.min.y);
                    m_Bounds.max.x = std::max(m_Bounds.max.x, box.max.x);
                    m_Bounds.max.y = std::max(m_Bounds.max.y, box.max.y);
                }

                const int x0 = Cell(box.min.x), x1 = Cell(box.max.x);
                const int y0 = Cell(box.min.y), y1 = Cell(box.max.y);
                const std::uint64_t span = static_cast<std::uint64_t>(x1 - x0 + 1) * static_cast<std::uint64_t>(y1 - y0 + 1);
                if (span > k_MaxCellsPerProxy) {
                    m_Flags[i] |= k_Large;
                    m_Large.push_back(i);
                    continue;
                }
                for (int y = y0; y <= y1; ++y) {
                    for (int x = x0; x <= x1; ++x) {
                        m_Entries.push_back({ Key(x, y), i });
                    }
                }
            }
            m_HasBounds = !first;
            Sort();
        }

        std::uint32_t Count() const noexcept { return m_Count; }
        bool IsValid(std::uint32_t proxy) const noexcept { return (m_Flags[proxy] & k_Valid) != 0; }
        const AABB& Box(std::uint32_t proxy) const noexcept { return m_Boxes[proxy]; }

        // Calls f(a, b) once for every pair of proxies whose AABBs overlap.
        template <typename F>
        void ForEachOverlappingPair(F&& f) const
        {
            const std::size_t n = m_Entries.size();
            std::size_t runStart = 0;
            while (runStart < n) {
                const std::uint64_t key = m_Entries[runStart].key;
                std::size_t runEnd = runStart + 1;
                while (runEnd < n && m_Entries[runEnd].key == key) ++runEnd;

                for (std::size_t i = runStart; i + 1 < runEnd; ++i) {
                    const std::uint32_t a = m_Entries[i].proxy;
                    const AABB& boxA = m_Boxes[a];
                    for (std::size_t j = i + 1; j < runEnd; ++j) {
                        const std::uint32_t b = m_Entries[j].proxy;
                        const AABB& boxB = m_Boxes[b];
                        if (!boxA.Intersects(boxB)) continue;
                        // A pair sharing several cells is handled only in the cell holding
                        // the lower corner of the overlap, so it is reported exactly once.
                        if (Key(Cell(std::max(boxA.min.x, boxB.min.x)), Cell(std::max(boxA.min.y, boxB.min.y))) != key) continue;
                        f(a, b);
                    }
                }
                runStart = runEnd;
            }

            for (std::size_t i = 0; i < m_Large.size(); ++i) {
                const std::uint32_t a = m_Large[i];
                const AABB& boxA = m_Boxes[a];
                for (std::uint32_t b = 0; b < m_Count; ++b) {
                    if (b == a || !IsValid(b)) continue;
                    // Large-vs-large pairs are reported by the lower index only.
                    if (IsLarge(b) && b < a) continue;
                    if (boxA.Intersects(m_Boxes[b])) f(a, b);
                }
            }
        }

        // Calls f(proxy) once for every proxy whose AABB overlaps `box`.
        template <typename F>
        void Query(const AABB& box, F&& f) const
        {
            if (!m_HasBounds || !box.Intersects(m_Bounds)) {
                return;
            }
            const int x0 = Cell(std::max(box.min.x, m_Bounds.min.x)), x1 = Cell(std::min(box.max.x, m_Bounds.max.x));
            const int y0 = Cell(std::max(box.min.y, m_Bounds.min.y)), y1 = Cell(std::min(box.max.y, m_Bounds.max.y));
            const std::uint64_t span = static_cast<std::uint64_t>(x1 - x0 + 1) * static_cast<std::uint64_t>(y1 - y0 + 1);
            if (span > m_Entries.size() + 16) {
                for (std::uint32_t p = 0; p < m_Count; ++p) {
                    if (IsValid(p) && box.Intersects(m_Boxes[p])) f(p);
                }
                return;
            }

            const std::uint32_t stamp = NextStamp();
            for (int y = y0; y <= y1; ++y) {
                for (int x = x0; x <= x1; ++x) {
                    VisitCell(x, y, stamp, [&](std::uint32_t p) {
                        if (box.Intersects(m_Boxes[p])) f(p);
                    });
                }
            }
            for (std::uint32_t p : m_Large) {
                if (box.Intersects(m_Boxes[p])) f(p);
            }
        }

        // Walks the cells along origin + dir * t (dir normalized) up to maxDistance and
        // calls test(proxy, bestT) for each proxy met; test returns the hit distance or
        // +inf. Stops once the next cell starts beyond the best hit. Returns the best t.
        template <typename F>
        float Raycast(const Vec2& origin, const Vec2& dir, float maxDistance, F&& test) const
        {
            float bestT = std::numeric_limits<float>::infinity();
            for (std::uint32_t p : m_Large) {
                bestT = std::min(bestT, test(p, bestT));
            }
            if (!m_HasBounds) {
                return bestT;
            }

            // Clip the ray against the populated area.
            float t0 = 0.0f;
            float t1 = std::min(maxDistance, std::numeric_limits<float>::max());
            const float o[2] = { origin.x, origin.y };
            const float d[2] = { dir.x, dir.y };
            const float lo[2] = { m_Bounds.min.x, m_Bounds.min.y };
            const float hi[2] = { m_Bounds.max.x, m_Bounds.max.y };
            for (int k = 0; k < 2; ++k) {
                if (std::fabs(d[k]) < 1e-12f) {
                    if (o[k] < lo[k] || o[k] > hi[k]) return bestT;
                    continue;
                }
                float ta = (lo[k] - o[k]) / d[k];
                float tb = (hi[k] - o[k]) / d[k];
                if (ta > tb) std::swap(ta, tb);
                t0 = std::max(t0, ta);
                t1 = std::min(t1, tb);
                if (t0 > t1) return bestT;
            }

            // Long rays through a sparse grid: testing every proxy is cheaper than walking.
            const float cellsAlong = (t1 - t0) * m_InvCell * 2.0f + 2.0f;
            if (cellsAlong > static_cast<float>(m_Count)) {
                for (std::uint32_t p = 0; p < m_Count; ++p) {
                    if (IsValid(p) && !IsLarge(p)) bestT = std::min(bestT, test(p, bestT));
                }
                return bestT;
            }

            const Vec2 start{ origin.x + dir.x * t0, origin.y + dir.y * t0 };
            int x = Cell(start.x);
            int y = Cell(start.y);
            const int stepX = dir.x > 0.0f ? 1 : (dir.x < 0.0f ? -1 : 0);
            const int stepY = dir.y > 0.0f ? 1 : (dir.y < 0.0f ? -1 : 0);
            const float inf = std::numeric_limits<float>::infinity();
            const float deltaX = stepX != 0 ? m_CellSize / std::fabs(dir.x) : inf;
            const float deltaY = stepY != 0 ? m_CellSize / std::fabs(dir.y) : inf;
            float nextX = stepX > 0 ? ((x + 1) * m_CellSize - origin.x) / dir.x
                        : stepX < 0 ? (x * m_CellSize - origin.x) / dir.x : inf;
            float nextY = stepY > 0 ? ((y + 1) * m_CellSize - origin.y) / dir.y
                        : stepY < 0 ? (y * m_CellSize - origin.y) / dir.y : inf;

            const std::uint32_t stamp = NextStamp();
            float cellEnter = t0;
            while (cellEnter <= t1 && cellEnter <= bestT) {
                VisitCell(x, y, stamp, [&](std::uint32_t p) {
                    bestT = std::min(bestT, test(p, bestT));
                });
                if (nextX < nextY) {
                    cellEnter = nextX;
                    nextX += deltaX;
                    x += stepX;
                }
                else {
                    cellEnter = nextY;
                    nextY += deltaY;
                    y += stepY;
                }
            }
            return bestT;
        }

    private:
        struct Entry
        {
            std::uint64_t key;
            std::uint32_t proxy;
        };

        int Cell(float v) const noexcept
        {
            const float c = std::floor(v * m_InvCell);
            constexpr float limit = 1.0e9f;
            return static_cast<int>(std::clamp(c, -limit, limit));
        }

        static std::uint64_t Key(int x, int y) noexcept
        {
            const std::uint32_t ux = static_cast<std::uint32_t>(x) ^ 0x80000000u;
            const std::uint32_t uy = static_cast<std::uint32_t>(y) ^ 0x80000000u;
            return (static_cast<std::uint64_t>(ux) << 32) | uy;
        }

        bool IsLarge(std::uint32_t proxy) const noexcept { return (m_Flags[proxy] & k_Large) != 0; }

        std::uint32_t NextStamp() const
        {
            if (m_Stamps.size() < m_Count) m_Stamps.resize(m_Count, 0);
            if (++m_StampValue == 0) {
                std::fill(m_Stamps.begin(), m_Stamps.end(), 0);
                m_StampValue = 1;
            }
            return m_StampValue;
        }

        template <typename F>
        void VisitCell(int x, int y, std::uint32_t stamp, F&& f) const
        {
            const std::uint64_t key = Key(x, y);
            auto it = std::lower_bound(m_Entries.begin(), m_Entries.end(), key,
                [](const Entry& e, std::uint64_t k) { return e.key < k; });
            for (; it != m_Entries.end() && it->key == key; ++it) {
                const std::uint32_t p = it->proxy;
                if (m_Stamps[p] == stamp) continue;
                m_Stamps[p] = stamp;
                f(p);
            }
        }

        // LSD radix sort on the 64-bit cell key; passes whose byte is equal for every
        // entry are skipped, so typical scenes need two to four passes.
        void Sort()
        {
            const std::size_t n = m_Entries.size();
            if (n < 64) {
                std::sort(m_Entries.begin(), m_Entries.end(),
                    [](const Entry& a, const Entry& b) { return a.key < b.key; });
                return;
            }
            std::uint32_t histograms[8][256] = {};
            for (const Entry& e : m_Entries) {
                for (int pass = 0; pass < 8; ++pass) {
                    ++histograms[pass][(e.key >> (pass * 8)) & 0xFFu];
                }
            }
            m_Scratch.resize(n);
            Entry* src = m_Entries.data();
            Entry* dst = m_Scratch.data();
            for (int pass = 0; pass < 8; ++pass) {
                std::uint32_t* hist = histograms[pass];
                const std::uint32_t firstByte = static_cast<std::uint32_t>((src[0].key >> (pass * 8)) & 0xFFu);
                if (hist[firstByte] == n) continue;
                std::uint32_t offset = 0;
                for (int b = 0; b < 256; ++b) {
                    const std::uint32_t c = hist[b];
                    hist[b] = offset;
                    offset += c;
                }
                for (std::size_t i = 0; i < n; ++i) {
                    dst[hist[(src[i].key >> (pass * 8)) & 0xFFu]++] = src[i];
                }
                std::swap(src, dst);
            }
            if (src != m_Entries.data()) {
                std::copy(src, src + n, m_Entries.data());
            }
        }

        std::vector<AABB> m_Boxes;
        static constexpr std::uint8_t k_Valid = 1;
        static constexpr std::uint8_t k_Large = 2;
        std::vector<std::uint8_t> m_Flags;
        std::vector<Entry> m_Entries;
        std::vector<Entry> m_Scratch;
        std::vector<std::uint32_t> m_Large;
        mutable std::vector<std::uint32_t> m_Stamps;
        mutable std::uint32_t m_StampValue = 0;
        AABB m_Bounds{};
        bool m_HasBounds = false;
        std::uint32_t m_Count = 0;
        float m_CellSize = 1.0f;
        float m_InvCell = 1.0f;
    };
}
