#include "TestRunner.hpp"

#include <cstdio>

int main()
{
    auto& registry = FunkePhys::Tests::Registry();
    std::printf("Running %zu tests...\n", registry.size());

    for (const auto& test : registry) {
        FunkePhys::Tests::CurrentTest() = test.name;
        const int failsBefore = FunkePhys::Tests::FailCount();
        test.fn();
        const int failsAfter = FunkePhys::Tests::FailCount();
        std::printf("  %s %s\n",
                    (failsAfter == failsBefore) ? "PASS" : "FAIL",
                    test.name);
    }

    const int failures = FunkePhys::Tests::FailCount();
    const int assertions = FunkePhys::Tests::AssertionCount();
    std::printf("\n%d / %d assertions passed across %zu tests. (%d failed)\n",
                assertions - failures, assertions, registry.size(), failures);

    return failures == 0 ? 0 : 1;
}
