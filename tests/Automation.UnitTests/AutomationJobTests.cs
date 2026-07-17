using Automation.Core.Jobs;

namespace Automation.UnitTests;

[TestFixture]
public sealed class AutomationJobTests
{
    [Test]
    public void Validate_RejectsNonHttpStartUrl()
    {
        var job = new AutomationJob("job-1", "demo", new Uri("file:///catalog"), 1);

        Assert.That(() => job.Validate(), Throws.ArgumentException);
    }

    [TestCase(0)]
    [TestCase(101)]
    public void Validate_RejectsMaxPagesOutsideAllowedRange(int maxPages)
    {
        var job = new AutomationJob("job-1", "demo", new Uri("https://example.test/catalog"), maxPages);

        Assert.That(() => job.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}

