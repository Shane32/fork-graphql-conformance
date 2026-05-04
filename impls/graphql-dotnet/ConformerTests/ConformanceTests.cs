using System.Text.Json;
using Conformer;
using Xunit;

namespace ConformerTests;

public class ConformanceTests
{
    /// <summary>
    /// Walk up from the test binary directory until we find registry.json (the repo root).
    /// </summary>
    static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "registry.json")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException(
            "Cannot find repo root: no registry.json found while walking up from " + AppContext.BaseDirectory);
    }

    public static IEnumerable<object[]> GetTestData()
    {
        var repoRoot = FindRepoRoot();
        var corpusDir = Path.Combine(repoRoot, "corpus");
        var expectedDir = Path.Combine(repoRoot, "corpus-expected");

        foreach (var (testCaseId, schemaPath, queryPath, variablesPath) in DiscoverCorpus(corpusDir))
        {
            var segments = testCaseId.Split('/');
            var expectedPath = Path.Combine(
                new[] { expectedDir }.Concat(segments).Append("result.json").ToArray());
            yield return new object[] { testCaseId, schemaPath, queryPath, variablesPath!, expectedPath };
        }
    }

    static IEnumerable<(string testCaseId, string schemaPath, string queryPath, string? variablesPath)> DiscoverCorpus(string corpusDir)
    {
        foreach (var schemaDir in Directory.GetDirectories(corpusDir).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal))
        {
            var schemaPath = Path.Combine(schemaDir, "schema.graphqls");
            if (!File.Exists(schemaPath)) continue;
            var schemaId = Path.GetFileName(schemaDir);

            foreach (var queryDir in Directory.GetDirectories(schemaDir).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal))
            {
                var queryPath = Path.Combine(queryDir, "query.graphql");
                if (!File.Exists(queryPath)) continue;
                var queryId = Path.GetFileName(queryDir);

                var varsDirs = Directory.GetDirectories(queryDir).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).ToArray();
                if (varsDirs.Length == 0)
                {
                    yield return ($"{schemaId}/{queryId}", schemaPath, queryPath, null);
                }
                else
                {
                    foreach (var varsDir in varsDirs)
                    {
                        var variablesPath = Path.Combine(varsDir, "variables.json");
                        if (!File.Exists(variablesPath)) continue;
                        var varsId = Path.GetFileName(varsDir);
                        yield return ($"{schemaId}/{queryId}/{varsId}", schemaPath, queryPath, variablesPath);
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(GetTestData))]
    public async Task Conformance(string testCaseId, string schemaPath, string queryPath, string? variablesPath, string expectedPath)
    {
        _ = testCaseId; // used for test display name only

        if (!File.Exists(expectedPath))
            return; // should not happen with a complete corpus-expected directory

        var expectedJson = File.ReadAllText(expectedPath);

        // null means the reference excluded this test (it returned GraphQL errors or had a harness error)
        if (expectedJson.Trim() == "null")
            return;

        var schema = File.ReadAllText(schemaPath);
        var query = File.ReadAllText(queryPath);
        JsonElement? variables = null;
        if (variablesPath != null)
        {
            var varsText = File.ReadAllText(variablesPath);
            var varsDoc = JsonDocument.Parse(varsText);
            if (varsDoc.RootElement.ValueKind == JsonValueKind.Object)
                variables = varsDoc.RootElement;
        }

        var request = new ExecuteRequest { Schema = schema, Query = query, Variables = variables };
        var actualJson = await GraphQLExecutor.ExecuteAsync(request);

        using var expectedDoc = JsonDocument.Parse(expectedJson);
        using var actualDoc = JsonDocument.Parse(actualJson);

        if (!UnorderedEqual(expectedDoc.RootElement, actualDoc.RootElement))
        {
            Assert.Fail(
                $"Test case: {testCaseId}\n" +
                $"Expected: {expectedJson}\n" +
                $"Actual:   {actualJson}");
        }
    }

    /// <summary>
    /// Compares two JsonElements with order-independent object key comparison
    /// (matching the conformer's unorderedEqual function in compare.js).
    /// Objects: keys are sorted then compared; Arrays: compared element-by-element.
    /// </summary>
    static bool UnorderedEqual(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;

        return a.ValueKind switch
        {
            JsonValueKind.Object => ObjectsEqual(a, b),
            JsonValueKind.Array => ArraysEqual(a, b),
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.Number => a.GetDouble() == b.GetDouble(),
            JsonValueKind.True or JsonValueKind.False => a.GetBoolean() == b.GetBoolean(),
            JsonValueKind.Null => true,
            _ => a.GetRawText() == b.GetRawText(),
        };
    }

    static bool ObjectsEqual(JsonElement a, JsonElement b)
    {
        var aProps = a.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        var bProps = b.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        if (aProps.Length != bProps.Length) return false;
        for (var i = 0; i < aProps.Length; i++)
        {
            if (aProps[i].Name != bProps[i].Name) return false;
            if (!UnorderedEqual(aProps[i].Value, bProps[i].Value)) return false;
        }
        return true;
    }

    static bool ArraysEqual(JsonElement a, JsonElement b)
    {
        var aItems = a.EnumerateArray().ToArray();
        var bItems = b.EnumerateArray().ToArray();
        if (aItems.Length != bItems.Length) return false;
        for (var i = 0; i < aItems.Length; i++)
        {
            if (!UnorderedEqual(aItems[i], bItems[i])) return false;
        }
        return true;
    }
}
