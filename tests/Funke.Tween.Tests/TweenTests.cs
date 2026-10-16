using System;
using System.Collections.Generic;
using Funke;
using Funke.Tween;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Funke.Tween.Tests;

public class TweenTests : IDisposable
{
    private sealed class TestEntity : Entity { }

    private readonly List<string> m_Logs = new();

    public TweenTests()
    {
        TweenManager.Reset();
        Tween.AutoUpdate = false;
        Tween.DefaultEase = Ease.Linear;
        TweenManager.IsAlive = static _ => true;
        Log.LogMessage += m_Logs.Add;
    }

    public void Dispose()
    {
        Log.LogMessage -= m_Logs.Add;
        TweenManager.Reset();
        Tween.DefaultEase = Ease.OutQuad;
    }

    private static void Step(float seconds, int frames = 1)
    {
        for (int i = 0; i < frames; i++) Tween.Update(seconds, seconds);
    }

    [Fact]
    public void Every_ease_starts_at_0_and_ends_at_1()
    {
        foreach (Ease ease in Enum.GetValues<Ease>())
        {
            Assert.Equal(0f, Easing.Evaluate(ease, 0f), 5);
            Assert.Equal(1f, Easing.Evaluate(ease, 1f), 5);
            if (ease.ToString().StartsWith("InOut", StringComparison.Ordinal))
                Assert.True(MathF.Abs(Easing.Evaluate(ease, 0.4999f) - Easing.Evaluate(ease, 0.5001f)) < 0.02f, ease.ToString());
        }
        Assert.Equal(0.25f, Easing.Evaluate(Ease.InQuad, 0.5f), 5);
        Assert.Equal(0.75f, Easing.Evaluate(Ease.OutQuad, 0.5f), 5);
        Assert.Equal(0.5f, Easing.Evaluate(Ease.InOutSine, 0.5f), 5);
        Assert.True(Easing.Evaluate(Ease.OutBack, 0.6f) > 1f);
        Assert.True(Easing.Evaluate(Ease.InBack, 0.2f) < 0f);
    }

    [Fact]
    public void A_tween_reaches_its_end_value_completes_once_and_is_recycled()
    {
        float x = 0f;
        int completed = 0, killed = 0, updates = 0;
        TweenHandle tween = Tween.To(() => x, v => x = v, 10f, 1f)
            .OnUpdate(() => updates++)
            .OnComplete(() => completed++)
            .OnKill(() => killed++);

        Assert.True(tween.IsActive);
        Assert.Equal(0f, x);
        Step(0.25f);
        Assert.Equal(2.5f, x, 4);
        Assert.Equal(0.25f, tween.Progress, 4);
        Step(0.25f, 3);
        Assert.Equal(10f, x);
        Assert.Equal(1, completed);
        Assert.Equal(1, killed);
        Assert.Equal(4, updates);
        Assert.False(tween.IsActive);
        Assert.Equal(0, Tween.ActiveCount);
    }

    [Fact]
    public void Delay_postpones_the_start_and_the_start_value_is_read_afterwards()
    {
        float x = 0f;
        int started = 0;
        Tween.To(() => x, v => x = v, 10f, 1f).SetDelay(0.5f).OnStart(() => started++);
        Step(0.25f);
        Assert.Equal(0f, x);
        Assert.Equal(0, started);
        x = 5f;
        Step(0.5f);
        Assert.Equal(1, started);
        Assert.Equal(6.25f, x, 4);
    }

    [Fact]
    public void Restart_loops_fire_step_complete_per_loop_and_complete_at_the_end()
    {
        float x = 0f;
        int steps = 0, completed = 0;
        Tween.To(() => x, v => x = v, 1f, 1f).SetLoops(3).OnStepComplete(() => steps++).OnComplete(() => completed++);
        Step(0.5f);
        Assert.Equal(0.5f, x, 4);
        Step(0.5f);
        Assert.Equal(0f, x, 4);
        Assert.Equal(1, steps);
        Step(0.5f, 4);
        Assert.Equal(1f, x, 4);
        Assert.Equal(3, steps);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Yoyo_returns_to_the_start_and_incremental_keeps_adding()
    {
        float yoyo = 0f, incremental = 0f;
        Tween.To(() => yoyo, v => yoyo = v, 1f, 1f).SetLoops(2, LoopType.Yoyo);
        Tween.To(() => incremental, v => incremental = v, 1f, 1f).SetLoops(3, LoopType.Incremental);
        Step(1.5f);
        Assert.Equal(0.5f, yoyo, 4);
        Assert.Equal(1.5f, incremental, 4);
        Step(0.5f);
        Assert.Equal(0f, yoyo, 4);
        Step(1f);
        Assert.Equal(3f, incremental, 4);
    }

    [Fact]
    public void Endless_loops_run_until_completed_or_killed()
    {
        float x = 0f;
        int completed = 0;
        TweenHandle tween = Tween.To(() => x, v => x = v, 1f, 1f).SetLoops(-1, LoopType.Yoyo).OnComplete(() => completed++);
        Step(0.5f, 41);
        Assert.True(tween.IsActive);
        Assert.Equal(0, completed);
        Assert.Equal(0.5f, x, 3);
        // Loop 20 plays forwards, so completing it ends on the end value.
        tween.Complete();
        Assert.Equal(1, completed);
        Assert.Equal(1f, x, 4);
        Assert.False(tween.IsActive);
    }

    [Fact]
    public void Relative_and_from_change_where_the_tween_starts_and_ends()
    {
        float a = 2f, b = 2f, c = 2f;
        Tween.To(() => a, v => a = v, 3f, 1f).SetRelative();
        Tween.To(() => b, v => b = v, 10f, 1f).From();
        Assert.Equal(10f, b);
        Tween.To(() => c, v => c = v, 10f, 1f).From(4f);
        Assert.Equal(4f, c);
        Step(0.5f);
        Assert.Equal(3.5f, a, 4);
        Assert.Equal(6f, b, 4);
        Assert.Equal(7f, c, 4);
        Step(0.5f);
        Assert.Equal(5f, a, 4);
        Assert.Equal(2f, b, 4);
        Assert.Equal(10f, c, 4);
    }

    [Fact]
    public void From_with_one_number_is_uniform_for_vectors_and_strict_otherwise()
    {
        Vector2 scale = new(1f, 1f);
        Tween.To(() => scale, v => scale = v, new Vector2(1f, 1f), 1f).From(0f);
        Assert.Equal(0f, scale.X);
        Assert.Equal(0f, scale.Y);
        Step(0.5f);
        Assert.Equal(0.5f, scale.Y, 4);

        Color color = Color.White;
        TweenHandle colorTween = Tween.To(() => color, v => color = v, Color.Black, 1f);
        Assert.Throws<InvalidOperationException>(() => colorTween.From(0.5f));
    }

    [Fact]
    public void Unscaled_tweens_run_while_game_time_is_frozen()
    {
        float scaled = 0f, unscaled = 0f;
        Tween.To(() => scaled, v => scaled = v, 1f, 1f);
        Tween.To(() => unscaled, v => unscaled = v, 1f, 1f).SetUnscaledTime();
        Tween.Update(0f, 0.5f);
        Assert.Equal(0f, scaled);
        Assert.Equal(0.5f, unscaled, 4);

        Tween.GlobalTimeScale = 2f;
        Tween.Update(0.25f, 0.25f);
        Assert.Equal(0.5f, scaled, 4);
        Assert.Equal(1f, unscaled, 4);
    }

    [Fact]
    public void Value_and_color_tweens_use_their_fixed_range()
    {
        float seen = -1f;
        Color color = Color.Black;
        Tween.Value(10f, 20f, 1f, v => seen = v);
        Tween.To(() => color, v => color = v, new Color(1f, 0.5f, 0f, 0f), 1f);
        Step(0.5f);
        Assert.Equal(15f, seen, 4);
        Assert.Equal(0.5f, color.R, 4);
        Assert.Equal(0.25f, color.G, 4);
        Assert.Equal(0.5f, color.A, 4);
    }

    [Fact]
    public void Custom_ease_function_and_curve_drive_the_progress()
    {
        float a = 0f, b = 0f;
        Tween.To(() => a, v => a = v, 1f, 1f).SetEase(t => t * t);
        var curve = new Graph
        {
            Keys = new List<CurveKey>
            {
                new(new Vector2(0f, 0f), new Vector2(-0.1f, -0.1f), new Vector2(0.1f, 0.1f)),
                new(new Vector2(1f, 1f), new Vector2(-0.1f, -0.1f), new Vector2(0.1f, 0.1f)),
            },
        };
        Tween.To(() => b, v => b = v, 1f, 1f).SetEase(curve);
        Step(0.5f);
        Assert.Equal(0.25f, a, 4);
        Assert.Equal(0.5f, b, 2);
    }

    [Fact]
    public void Delayed_call_fires_once_after_its_time_and_can_be_cancelled()
    {
        int fired = 0, cancelled = 0;
        Tween.Delay(0.5f, () => fired++);
        TweenHandle cancel = Tween.Delay(0.5f, () => cancelled++);
        Step(0.25f);
        Assert.Equal(0, fired);
        cancel.Kill();
        Step(0.3f);
        Assert.Equal(1, fired);
        Assert.Equal(0, cancelled);
        Assert.Equal(0, Tween.ActiveCount);
    }

    [Fact]
    public void Pause_play_restart_rewind_and_goto_control_a_tween()
    {
        float x = 0f;
        TweenHandle tween = Tween.To(() => x, v => x = v, 1f, 1f).SetAutoKill(false);
        Step(0.25f);
        tween.Pause();
        Assert.False(tween.IsPlaying);
        Step(0.5f);
        Assert.Equal(0.25f, x, 4);
        tween.Play();
        Step(0.25f);
        Assert.Equal(0.5f, x, 4);

        tween.Goto(0.75f);
        Assert.Equal(0.75f, x, 4);
        Assert.False(tween.IsPlaying);
        tween.Goto(0.75f, andPlay: true);
        Step(1f);
        Assert.True(tween.IsComplete);
        Assert.True(tween.IsActive);
        Assert.Equal(1f, x, 4);

        tween.Restart();
        Assert.Equal(0f, x, 4);
        Step(0.5f);
        Assert.Equal(0.5f, x, 4);

        tween.Rewind();
        Assert.Equal(0f, x, 4);
        Step(0.5f);
        Assert.Equal(0f, x, 4);
        tween.Kill();
        Assert.False(tween.IsActive);
    }

    [Fact]
    public void Kill_with_complete_jumps_to_the_end_and_fires_on_complete()
    {
        float x = 0f;
        int completed = 0;
        TweenHandle tween = Tween.To(() => x, v => x = v, 4f, 1f).OnComplete(() => completed++);
        Step(0.25f);
        tween.Kill(complete: true);
        Assert.Equal(4f, x);
        Assert.Equal(1, completed);
        Assert.False(tween.IsActive);
    }

    [Fact]
    public void A_stale_handle_never_controls_the_recycled_tween()
    {
        float a = 0f, b = 0f;
        TweenHandle first = Tween.To(() => a, v => a = v, 1f, 0.1f);
        Step(0.2f);
        Assert.False(first.IsActive);

        TweenHandle second = Tween.To(() => b, v => b = v, 1f, 1f);
        first.Kill();
        first.Pause();
        Assert.True(second.IsActive);
        Assert.True(second.IsPlaying);
        Assert.NotEqual(first, second);
        Step(0.5f);
        Assert.Equal(0.5f, b, 4);
        Assert.False(default(TweenHandle).IsActive);
    }

    [Fact]
    public void Tweens_are_found_by_id()
    {
        float x = 0f;
        Tween.To(() => x, v => x = v, 1f, 1f).SetId("hud");
        Tween.To(() => x, v => x = v, 1f, 1f).SetId("hud");
        Tween.To(() => x, v => x = v, 1f, 1f).SetId("other");
        Assert.True(Tween.IsTweening("hud"));
        Assert.Equal(2, Tween.Pause("hud"));
        Assert.Equal(2, Tween.Kill("hud"));
        Assert.False(Tween.IsTweening("hud"));
        Assert.True(Tween.IsTweening("other"));
    }

    [Fact]
    public void A_destroyed_linked_entity_kills_its_tweens()
    {
        var entity = new TestEntity();
        var dead = new HashSet<object>(ReferenceEqualityComparer.Instance);
        TweenManager.IsAlive = e => !dead.Contains(e);

        float x = 0f;
        int writes = 0, killed = 0;
        TweenHandle tween = Tween.To(() => x, v => { x = v; writes++; }, 1f, 1f).SetLink(entity).OnKill(() => killed++);
        Step(0.25f);
        Assert.Equal(1, writes);
        dead.Add(entity);
        Step(0.25f);
        Assert.Equal(1, writes);
        Assert.Equal(1, killed);
        Assert.False(tween.IsActive);
    }

    [Fact]
    public void A_throwing_setter_kills_the_tween_and_a_throwing_callback_only_logs()
    {
        TweenHandle broken = Tween.To(() => 0f, _ => throw new ArgumentException("boom"), 1f, 1f);
        float x = 0f;
        TweenHandle noisy = Tween.To(() => x, v => x = v, 1f, 1f).OnUpdate(() => throw new ArgumentException("callback"));
        Step(0.25f);
        Assert.False(broken.IsActive);
        Assert.True(noisy.IsActive);
        Step(0.25f);
        Assert.Equal(0.5f, x, 4);
        Assert.Contains(m_Logs, line => line.Contains("boom"));
        Assert.Contains(m_Logs, line => line.Contains("OnUpdate callback threw"));
    }

    [Fact]
    public void Sequence_places_append_join_interval_insert_and_callbacks_on_one_timeline()
    {
        float a = 0f, b = 0f, c = 0f, d = 0f;
        var log = new List<string>();
        Sequence sequence = Tween.Sequence()
            .Append(Tween.To(() => a, v => a = v, 1f, 1f))
            .Join(Tween.To(() => b, v => b = v, 1f, 0.5f))
            .AppendInterval(0.5f)
            .AppendCallback(() => log.Add("interval done"))
            .Append(Tween.To(() => c, v => c = v, 2f, 1f))
            .Insert(0.25f, Tween.To(() => d, v => d = v, 1f, 0.5f))
            .InsertCallback(0f, () => log.Add("start"))
            .OnComplete(() => log.Add("complete"));

        Assert.Equal(2.5f, sequence.Duration, 4);
        Assert.Equal(1, Tween.ActiveCount);

        Step(0.25f);
        Assert.Equal(new[] { "start" }, log);
        Assert.Equal(0.25f, a, 4);
        Assert.Equal(0.5f, b, 4);
        Assert.Equal(0f, d, 4);
        Step(0.5f);
        Assert.Equal(0.75f, a, 4);
        Assert.Equal(1f, b, 4);
        Assert.Equal(1f, d, 4);
        Step(0.5f);
        Assert.Equal(1f, a, 4);
        Assert.Equal(0f, c, 4);
        Assert.Single(log);
        Step(0.5f);
        Assert.Equal(new[] { "start", "interval done" }, log);
        Assert.Equal(0.5f, c, 4);
        Step(0.75f);
        Assert.Equal(2f, c, 4);
        Assert.Equal(new[] { "start", "interval done", "complete" }, log);
        Assert.False(sequence.IsActive);
        Assert.Equal(0, Tween.ActiveCount);
    }

    [Fact]
    public void Sequential_tweens_on_one_value_chain_and_restart_loops_rewind_them()
    {
        float x = 0f;
        int callbacks = 0;
        Tween.Sequence()
            .Append(Tween.To(() => x, v => x = v, 1f, 1f))
            .Append(Tween.To(() => x, v => x = v, 3f, 1f))
            .InsertCallback(0.5f, () => callbacks++)
            .SetLoops(2);
        Step(1.5f);
        Assert.Equal(2f, x, 4);
        Step(1f);
        Assert.Equal(0.5f, x, 4);
        Assert.Equal(2, callbacks);
        Step(1.5f);
        Assert.Equal(3f, x, 4);
        Assert.Equal(0, Tween.ActiveCount);
    }

    [Fact]
    public void Yoyo_sequences_play_their_children_backwards()
    {
        float x = 0f;
        int childCompletes = 0;
        Tween.Sequence()
            .Append(Tween.To(() => x, v => x = v, 1f, 1f).OnComplete(() => childCompletes++))
            .Append(Tween.To(() => x, v => x = v, 3f, 1f))
            .SetLoops(2, LoopType.Yoyo);
        Step(2f);
        Assert.Equal(3f, x, 4);
        Step(0.5f);
        Assert.Equal(2f, x, 4);
        Step(1f);
        Assert.Equal(0.5f, x, 4);
        Step(0.5f);
        Assert.Equal(0f, x, 4);
        Assert.Equal(1, childCompletes);
    }

    [Fact]
    public void A_sequence_child_whose_setter_fails_is_skipped_and_the_rest_plays_on()
    {
        float x = 0f;
        int failures = 0;
        Tween.Sequence()
            .Append(Tween.To(() => 0f, _ => { failures++; throw new ArgumentException("gone"); }, 1f, 1f))
            .Join(Tween.To(() => x, v => x = v, 1f, 1f));
        Step(0.25f, 4);
        Assert.Equal(1, failures);
        Assert.Equal(1f, x, 4);
    }

    [Fact]
    public void Sequences_nest_and_lock_after_they_start()
    {
        float x = 0f, y = 0f;
        Sequence inner = Tween.Sequence().Append(Tween.To(() => x, v => x = v, 1f, 1f));
        Sequence outer = Tween.Sequence()
            .AppendInterval(1f)
            .Append(inner)
            .Append(Tween.To(() => y, v => y = v, 1f, 1f));
        Assert.Equal(1, Tween.ActiveCount);
        Step(1.5f);
        Assert.Equal(0.5f, x, 4);
        Assert.Equal(0f, y, 4);
        Step(1f);
        Assert.Equal(1f, x, 4);
        Assert.Equal(0.5f, y, 4);

        TweenHandle late = Tween.To(() => y, v => y = v, 5f, 1f);
        Assert.Throws<InvalidOperationException>(() => outer.Append(late));
    }

    [Fact]
    public void Tweens_inside_a_sequence_ignore_direct_control()
    {
        float x = 0f;
        TweenHandle child = Tween.To(() => x, v => x = v, 1f, 1f);
        Tween.Sequence().Append(child);
        child.Kill();
        child.Pause();
        Step(1f);
        Assert.Equal(1f, x, 4);
        Assert.Contains(m_Logs, line => line.Contains("part of a sequence"));
    }

    [Fact]
    public void Shake_stays_within_strength_and_settles_on_the_current_base()
    {
        Vector3 p = new(5f, 0f, 0f);
        Tween.Shake(() => p, v => p = v, 1f, new Vector3(1f, 0.5f, 0f), vibrato: 12);
        float maxX = 0f, maxY = 0f;
        for (int i = 0; i < 30; i++)
        {
            Step(1f / 60f);
            maxX = MathF.Max(maxX, MathF.Abs(p.X - 5f));
            maxY = MathF.Max(maxY, MathF.Abs(p.Y));
            Assert.Equal(0f, p.Z);
        }
        Assert.True(maxX > 0.05f && maxX <= 1.0001f, $"x offset {maxX}");
        Assert.True(maxY <= 0.5001f, $"y offset {maxY}");

        p = new Vector3(15f, 0f, 0f);
        Step(1f / 60f, 40);
        Assert.Equal(15f, p.X, 4);
        Assert.Equal(0f, p.Y, 4);
    }

    [Fact]
    public void Punch_kicks_towards_the_punch_and_returns()
    {
        float x = 1f, highest = 1f, lowest = 1f;
        Tween.Punch(() => x, v => x = v, 2f, 1f, vibrato: 1, elasticity: 0f);
        for (int i = 0; i < 20; i++)
        {
            Step(0.05f);
            highest = MathF.Max(highest, x);
            lowest = MathF.Min(lowest, x);
        }
        Assert.True(highest > 1.5f);
        Assert.Equal(1f, lowest, 4);
        Assert.Equal(1f, x, 4);
    }

    [Fact]
    public void Killing_a_shake_midway_removes_its_offset()
    {
        Vector3 p = Vector3.Zero;
        TweenHandle shake = Tween.Shake(() => p, v => p = v, 1f, new Vector3(1f, 1f, 1f));
        Step(0.23f);
        Assert.NotEqual(Vector3.Zero, p);
        shake.Kill();
        Assert.Equal(0f, p.X, 4);
        Assert.Equal(0f, p.Y, 4);
        Assert.Equal(0f, p.Z, 4);
    }

    [Fact]
    public void Running_tweens_allocate_nothing_per_frame()
    {
        Tween.Prewarm();
        float a = 0f, b = 0f;
        Vector3 p = Vector3.Zero;
        Tween.To(() => a, v => a = v, 1f, 0.5f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.OutElastic);
        Tween.Shake(() => p, v => p = v, 0.7f, new Vector3(1f, 1f, 1f)).SetLoops(-1);
        Tween.Sequence()
            .Append(Tween.To(() => b, v => b = v, 1f, 0.3f))
            .AppendCallback(static () => { })
            .Append(Tween.To(() => b, v => b = v, 0f, 0.3f))
            .SetLoops(-1);
        Step(1f / 60f, 120);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Step(1f / 60f, 600);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Play_mode_exit_drops_every_tween_without_callbacks()
    {
        float x = 0f;
        int killed = 0;
        TweenHandle tween = Tween.To(() => x, v => x = v, 1f, 1f).OnKill(() => killed++);
        Step(0.5f);
        TweenManager.Reset();
        Assert.False(tween.IsActive);
        Assert.Equal(0, killed);
        Assert.Equal(0, Tween.ActiveCount);
    }
}
