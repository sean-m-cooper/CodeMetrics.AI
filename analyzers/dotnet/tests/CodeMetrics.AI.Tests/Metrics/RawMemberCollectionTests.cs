using System.Text.Json;
using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMetrics.AI.Tests.Metrics;

public class RawMemberCollectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RawMembers_PreserveNamesCountsAndPartialBodiesInEitherDeclarationOrder(bool implementationFirst)
    {
        const string declaration = """
            namespace Demo;
            public partial class Widget {
                private int first = 1, second = 2;
                public event System.Action First, Second;
                public Widget() { }
                ~Widget() { }
                public int this[int index] => index > 0 ? 1 : 0;
                public static Widget operator +(Widget left, Widget right) => left;
                public static implicit operator int(Widget value) => value.first;
                public partial int Run(bool yes);
                public partial int Value { get; }
                public int Run(int number) => number;
                public class Nested { public void NestedOnly() { } }
            }
            """;
        const string implementation = """
            namespace Demo;
            public partial class Widget {
                public partial int Run(bool yes) { if (yes) return 1; return 0; }
                public partial int Value => 1;
            }
            """;
        var root = Path.Combine(Path.GetTempPath(), "RawMemberCollection");
        var sources = implementationFirst ? new[] { implementation, declaration } : [declaration, implementation];
        var trees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(source,
            path: Path.Combine(root, index + ".cs"), cancellationToken: TestContext.Current.CancellationToken)).ToArray();
        var (_, _, template) = RoslynTestHelper.CompileCode("");
        var compilation = template.RemoveAllSyntaxTrees().AddSyntaxTrees(trees);
        compilation.GetDiagnostics(TestContext.Current.CancellationToken).Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var result = MetricsCollector.Collect("App", compilation, root);
        var members = result.Members.Where(m => m.TypeId == "global::Demo.Widget").ToArray();
        var type = result.Types.Single(t => t.TypeId == "global::Demo.Widget");

        members.Should().HaveCount(10);
        members.Count(m => m.Member == "Run").Should().Be(2);
        members.Where(m => m.Member == "Run").Select(m => m.CyclomaticComplexity).Should().BeEquivalentTo([1, 2]);
        members.Should().ContainSingle(m => m.Member == "Widget");
        members.Should().ContainSingle(m => m.Member == "~Widget");
        members.Should().ContainSingle(m => m.Member == "this[]" && m.CyclomaticComplexity == 2);
        members.Should().ContainSingle(m => m.Member.Contains("operator +"));
        members.Should().ContainSingle(m => m.Member.Contains("implicit operator int"));
        members.Should().ContainSingle(m => m.Member == "Value" && m.HasBody);
        members.Should().NotContain(m => m.Member == "second" || m.Member == "Second" || m.Member == "NestedOnly");
        members.Where(m => !m.HasBody).Select(m => m.Member).Should().Equal("first", "First");
        members.Where(m => !m.HasBody).Should().AllSatisfy(m =>
        {
            m.CyclomaticComplexity.Should().Be(1);
            m.MaintainabilityIndex.Should().Be(100);
            m.LinesOfSource.Should().Be(0);
            m.LinesOfExecutable.Should().Be(0);
        });
        type.MemberCount.Should().Be(10);
        type.CyclomaticComplexity.Should().Be(11);
        type.MaxMemberCyclomaticComplexity.Should().Be(2);
        result.Members.Should().ContainSingle(m => m.TypeId == "global::Demo.Widget.Nested" && m.Member == "NestedOnly");

        var reordered = MetricsCollector.Collect("App", template.RemoveAllSyntaxTrees().AddSyntaxTrees(trees.Reverse()), root);
        JsonSerializer.Serialize(reordered.Members).Should().Be(JsonSerializer.Serialize(result.Members));
        JsonSerializer.Serialize(reordered.Types).Should().Be(JsonSerializer.Serialize(result.Types));
    }
}
