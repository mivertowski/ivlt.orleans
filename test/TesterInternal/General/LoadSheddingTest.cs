using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace UnitTests.General
{
    /// <summary>
    /// Tests for load shedding functionality when the gateway is overloaded.
    /// </summary>
    // if we parallelize tests, each test should run in isolation 
    public class LoadSheddingTest : OrleansTestingBase, IClassFixture<LoadSheddingTest.Fixture>
    {
        private readonly Fixture fixture;

        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.Options.InitialSilosCount = 1;
                builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            }
        }

        internal class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder.AddMemoryGrainStorage("MemoryStore")
                    .AddMemoryGrainStorageAsDefault()
                    .Configure<LoadSheddingOptions>(options => options.LoadSheddingEnabled = true);
            }
        }

        public LoadSheddingTest(Fixture fixture)
        {
            this.fixture = fixture;
            HostedCluster = fixture.HostedCluster;
        }

        public TestCluster HostedCluster { get; }

        [Fact, TestCategory("Functional"), TestCategory("LoadShedding")]
        public async Task LoadSheddingBasic()
        {
            ISimpleGrain grain = this.fixture.GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.Next(), SimpleGrain.SimpleGrainNamePrefix);

            var latchPeriod = TimeSpan.FromSeconds(1);
            await this.HostedCluster.Client.GetTestHooks(this.HostedCluster.Primary).LatchIsOverloaded(true, latchPeriod);

            // Do not accept message in overloaded state
            await Assert.ThrowsAsync<GatewayTooBusyException>(() =>
                grain.SetA(5));
            await Task.Delay(latchPeriod.Multiply(1.1)); // wait for latch to reset
        }

        [Fact, TestCategory("Functional"), TestCategory("LoadShedding")]
        public async Task LoadSheddingComplex()
        {
            ISimpleGrain grain = this.fixture.GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.Next(), SimpleGrain.SimpleGrainNamePrefix);

            this.fixture.Logger.LogInformation("Acquired grain reference");

            await grain.SetA(1);
            this.fixture.Logger.LogInformation("First set succeeded");

            var latchPeriod = TimeSpan.FromSeconds(1);
            await this.HostedCluster.Client.GetTestHooks(this.HostedCluster.Primary).LatchIsOverloaded(true, latchPeriod);

            // Do not accept message in overloaded state
            await Assert.ThrowsAsync<GatewayTooBusyException>(() =>
                grain.SetA(2));

            await Task.Delay(latchPeriod.Multiply(1.1)); // wait for latch to reset

            this.fixture.Logger.LogInformation("Second set was shed");

            await this.HostedCluster.Client.GetTestHooks(this.HostedCluster.Primary).LatchIsOverloaded(false, latchPeriod);

            // Simple request after overload is cleared should succeed
            await grain.SetA(4);
            this.fixture.Logger.LogInformation("Third set succeeded");
            await Task.Delay(latchPeriod.Multiply(1.1)); // wait for latch to reset
        }
    }
}
