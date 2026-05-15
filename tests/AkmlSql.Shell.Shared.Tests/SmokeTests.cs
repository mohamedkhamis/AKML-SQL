using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    public class SmokeTests
    {
        [StaFact]
        public void StaFact_Runs_OnSTAThread()
        {
            // STAFact ensures this test runs on a single-threaded apartment thread,
            // which WPF UI tests require. If this asserts, the test infrastructure works.
            Assert.Equal(System.Threading.ApartmentState.STA, System.Threading.Thread.CurrentThread.GetApartmentState());
        }
    }
}
