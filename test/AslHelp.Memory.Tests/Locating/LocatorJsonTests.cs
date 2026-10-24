using AslHelp.Memory.Locating;
using AslHelp.Memory.Scanning;
using NUnit.Framework;
using System.Text.Json;

namespace AslHelp.Memory.Tests.Locating;

[TestFixture]
public class LocatorJsonTests
{
    private const string SceneManager = """
        {
          "SceneManager_Get_Custom_PropSceneCount": [
            { "forward": [
                "48 8B 05 ????????",  // mov rax, [sceneManager]
                "8B 40 18",           // mov eax, [rax+0x18]
                "C3"                  // ret
              ], "offset": 3 },
            { "read": "global" }
          ]
        }
        """;

    private const string MonoDomain = """
        {
          "2021.3 release": [
            { "export": "mono_assembly_foreach" },
            { "forward": "48 8B 0D", "within": "0x100", "offset": "0x3" },
            { "read": "global" }
          ],
          "2021.3 master": [
            { "read": "pointer", "offset": "0x8" },
            { "offset": "0x10" }
          ]
        }
        """;

    private static readonly JsonSerializerOptions _options = new() { ReadCommentHandling = JsonCommentHandling.Skip };

    // The readme's example reads into 1 route with a whole-module search and a global read.
    [Test]
    public void Deserialize_ReadmeExample_ReadsTheSteps()
    {
        var locator = Parse(SceneManager);

        Assert.That(
            locator.Routes[0].Steps,
            Is.EqualTo(new RouteStep[]
            {
                new SearchStep(Scan.For("48 8B 05 ?? ?? ?? ?? 8B 40 18 C3"), 3),
                new ReadStep(ReadKind.Global),
            }));
    }

    // Routes keep the order of the document.
    [Test]
    public void Deserialize_SeveralRoutes_KeepsTheirOrder()
    {
        var locator = Parse(MonoDomain);

        Assert.That(locator.ToString(), Is.EqualTo("2021.3 release or 2021.3 master"));
    }

    // Each step kind reads with its settings.
    [Test]
    public void Deserialize_EachStepKind_ReadsItsSettings()
    {
        var locator = Parse(MonoDomain);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                locator.Routes[0].Steps,
                Is.EqualTo(new RouteStep[]
                {
                    new ExportStep("mono_assembly_foreach"),
                    new SearchStep(Scan.Forward(0x100).For("48 8B 0D"), 3),
                    new ReadStep(ReadKind.Global),
                }));
            Assert.That(
                locator.Routes[1].Steps,
                Is.EqualTo(new RouteStep[] { new ReadStep(ReadKind.Pointer, Offset: 8), new OffsetStep(0x10) }));
        }
    }

    // Writing and reading again gives the same routes.
    [TestCase(SceneManager)]
    [TestCase(MonoDomain)]
    public void Serialize_ReferenceExample_RoundTrips(string json)
    {
        var locator = Parse(json);
        var again = Parse(JsonSerializer.Serialize(locator, _options));

        Assert.That(again.Routes, Has.Count.EqualTo(locator.Routes.Count));
        for (int i = 0; i < locator.Routes.Count; i++)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(again.Routes[i].Name, Is.EqualTo(locator.Routes[i].Name));
                Assert.That(again.Routes[i].Steps, Is.EqualTo(locator.Routes[i].Steps));
            }
        }
    }

    // A step writes its numbers as hex, a negative one as a JSON number and a default size not at all.
    [Test]
    public void Serialize_Steps_WritesTheShortForm()
    {
        MemoryLocator locator = new(new LocatorRoute(
            "release",
            new SearchStep(Scan.Backward(0x40).For("48 8b ?5"), -2),
            new ReadStep(ReadKind.Branch, Size: 5),
            new ReadStep(ReadKind.Global)));

        Assert.That(
            JsonSerializer.Serialize(locator),
            Is.EqualTo("""{"release":[{"backward":"48 8B ?5","within":"0x40","offset":-2},{"read":"branch","size":"0x5"},{"read":"global"}]}"""));
    }

    // A decimal number reads the same as hex.
    [Test]
    public void Deserialize_DecimalOffset_ReadsTheSameAsHex()
    {
        Assert.That(Parse("""{ "a": [ { "offset": 16 } ] }""").Routes[0].Steps[0], Is.EqualTo(new OffsetStep(0x10)));
    }

    // The error of an invalid step includes the name of its route.
    [Test]
    public void Deserialize_InvalidStep_NamesTheRoute()
    {
        Assert.That(
            () => Parse("""{ "release": [ { "jump": "48" } ] }"""),
            Throws.InstanceOf<JsonException>().With.Message.Contains("The route 'release' is invalid.").And.Message.Contains("'jump'"));
    }

    // Each malformed step fails with its own message.
    [TestCase("""[ { "forward": "48 8B 0D" } ]""", "named routes")]
    [TestCase("""{ "a": [] }""", "at least 1 step")]
    [TestCase("""{}""", "at least 1 route")]
    [TestCase("""{ "a": "48" }""", "array of steps")]
    [TestCase("""{ "a": [ { "export": "x", "read": "pointer" } ] }""", "2 steps")]
    [TestCase("""{ "a": [ { "within": "0x40" } ] }""", "needs 1 key")]
    [TestCase("""{ "a": [ { "read": "pointer", "size": 8 } ] }""", "Only 'global' and 'branch'")]
    [TestCase("""{ "a": [ { "read": "absolute" } ] }""", "global, branch or pointer")]
    [TestCase("""{ "a": [ { "forward": "48 8B 0", "within": 64 } ] }""", "isn't a signature")]
    [TestCase("""{ "a": [ { "forward": [], "within": 64 } ] }""", "empty array")]
    [TestCase("""{ "a": [ { "forward": "48", "within": 0 } ] }""", "positive size")]
    [TestCase("""{ "a": [ { "export": "x", "within": 64 } ] }""", "doesn't take 'within'")]
    [TestCase("""{ "a": [ { "read": "pointer" }, { "export": "x" } ] }""", "has to be the first step")]
    [TestCase("""{ "a": [ { "read": "pointer" }, { "forward": "48" } ] }""", "has to be the first step")]
    public void Deserialize_MalformedDocument_Throws(string json, string message)
    {
        Assert.That(() => Parse(json), Throws.InstanceOf<JsonException>().With.Message.Contains(message));
    }

    private static MemoryLocator Parse(string json)
    {
        return JsonSerializer.Deserialize<MemoryLocator>(json, _options)!;
    }
}
