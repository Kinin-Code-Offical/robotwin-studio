using System;
using System.Threading;
using System.Threading.Tasks;
using RobotTwin.CoreSim.Mechanics;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[perf] The solver's work team: every index once per pass, errors carried back, one caller at a time.</summary>
    public class WorkTeamTests
    {
        private static void RunOrQueue(int count, int chunk, Action<int, int> body)
        {
            // Another test may hold the team (tests run in parallel): wait for it, as a solver would fall back.
            while (!WorkTeam.Shared.TryRun(count, chunk, body)) Thread.Yield();
        }

        [Fact]
        public void EveryIndexIsDoneOncePerPassOverManyPasses()
        {
            if (WorkTeam.Shared.Workers == 0) return;
            var hits = new int[3001];
            for (int pass = 1; pass <= 400; pass++)
            {
                int count = 1 + pass * 7 % 3001;
                RunOrQueue(count, 1 + pass % 97, (from, to) =>
                {
                    for (int i = from; i < to; i++) Interlocked.Increment(ref hits[i]);
                });
                for (int i = 0; i < hits.Length; i++)
                {
                    Assert.Equal(i < count ? 1 : 0, hits[i]);
                    hits[i] = 0;
                }
            }
        }

        [Fact]
        public void AnErrorInAChunkComesBackToTheCallerAndTheTeamGoesOn()
        {
            if (WorkTeam.Shared.Workers == 0) return;
            var error = Assert.Throws<AggregateException>(() => RunOrQueue(1000, 10, (from, to) =>
            {
                if (from <= 500 && 500 < to) throw new InvalidOperationException("chunk 50");
            }));
            Assert.IsType<InvalidOperationException>(error.InnerException);
            int sum = 0;
            RunOrQueue(1000, 10, (from, to) => Interlocked.Add(ref sum, to - from));
            Assert.Equal(1000, sum);
        }

        [Fact]
        public void APassAskedForInsideAPassIsRefusedSoTheCallerCanDoItAnotherWay()
        {
            if (WorkTeam.Shared.Workers == 0) return;
            int refused = 0;
            RunOrQueue(64, 8, (from, to) =>
            {
                if (!WorkTeam.Shared.TryRun(4, 1, (a, b) => { })) Interlocked.Increment(ref refused);
            });
            Assert.Equal(8, refused);
        }

        [Fact]
        public void TwoThreadsAskingAtOnceBothGetTheirWorkDone()
        {
            if (WorkTeam.Shared.Workers == 0) return;
            int a = 0, b = 0;
            Parallel.Invoke(
                () => { for (int k = 0; k < 200; k++) RunOrQueue(500, 16, (f, t) => Interlocked.Add(ref a, t - f)); },
                () => { for (int k = 0; k < 200; k++) RunOrQueue(500, 16, (f, t) => Interlocked.Add(ref b, t - f)); });
            Assert.Equal(200 * 500, a);
            Assert.Equal(200 * 500, b);
        }
    }
}
