using Automation.Worker.Jobs;

namespace Automation.UnitTests;

[TestFixture]
public sealed class JobLineParserTests
{
    private readonly JobLineParser parser = new();

    [Test]
    public void Parse_ValidJob_ReturnsValidatedDomainJob()
    {
        const string json = """{"jobId":"catalog-2026-001","target":"demo-catalog","startUrl":"http://demo-site:8080/catalog","maxPages":2}""";

        var result = parser.Parse(json, lineNumber: 1);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Job!.JobId, Is.EqualTo("catalog-2026-001"));
            Assert.That(result.Job.MaxPages, Is.EqualTo(2));
        });
    }

    [Test]
    public void Parse_InvalidJob_ReportsErrorWithoutCreatingJob()
    {
        const string json = """{"jobId":"","target":"demo-catalog","startUrl":"not-a-url","maxPages":0}""";

        var result = parser.Parse(json, lineNumber: 7);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Job, Is.Null);
            Assert.That(result.LineNumber, Is.EqualTo(7));
            Assert.That(result.Error, Is.Not.Empty);
        });
    }

    [Test]
    public void Parse_Utf8BomAtStart_AcceptsFirstStandardInputRecord()
    {
        const string json = "\uFEFF" + """{"jobId":"bom-job","target":"demo-catalog","startUrl":"http://demo-site:8080/catalog","maxPages":1}""";

        var result = parser.Parse(json, lineNumber: 1);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Job!.JobId, Is.EqualTo("bom-job"));
    }
}
