using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Output;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Output;

public class CsvWriterTests
{
    private const string ExpectedHeader =
        "Scope,Project,Namespace,Type,Member,Maintainability Index,Cyclomatic Complexity,Depth of Inheritance,Class Coupling,Lines of Source code,Lines of Executable code";

    [Fact]
    public async Task WriteAsync_HeaderLine_MatchesVsFormat()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var types = new List<TypeMetrics>
            {
                new() { Project = "Proj", Namespace = "NS", Type = "MyClass", FilePath = "file.cs" }
            };
            var members = new List<MemberMetrics>();

            await CsvWriter.WriteAsync(tempFile, types, members);

            var lines = await File.ReadAllLinesAsync(tempFile);
            lines[0].Should().Be(ExpectedHeader);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WriteAsync_TypeRow_HasCorrectFormat()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var types = new List<TypeMetrics>
            {
                new()
                {
                    Project = "MyProj",
                    Namespace = "MyNS",
                    Type = "MyType",
                    FilePath = "file.cs",
                    MaintainabilityIndex = 85,
                    CyclomaticComplexity = 3,
                    DepthOfInheritance = 2,
                    ClassCoupling = 5,
                    LinesOfSource = 100,
                    LinesOfExecutable = 60
                }
            };
            var members = new List<MemberMetrics>();

            await CsvWriter.WriteAsync(tempFile, types, members);

            var lines = await File.ReadAllLinesAsync(tempFile);
            // Line 0 is header, line 1 is the type row
            lines[1].Should().Be("Type,MyProj,MyNS,MyType,,85,3,2,5,100,60");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WriteAsync_TypeRow_EmptyMemberColumn()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var types = new List<TypeMetrics>
            {
                new() { Project = "P", Namespace = "N", Type = "T", FilePath = "f.cs" }
            };
            var members = new List<MemberMetrics>();

            await CsvWriter.WriteAsync(tempFile, types, members);

            var lines = await File.ReadAllLinesAsync(tempFile);
            var cols = lines[1].Split(',');
            // Scope=Type, Project=P, Namespace=N, Type=T, Member="" (empty), then metrics
            cols[0].Should().Be("Type");
            cols[4].Should().Be(string.Empty);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WriteAsync_MemberRow_HasCorrectFormat()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var types = new List<TypeMetrics>
            {
                new()
                {
                    Project = "MyProj",
                    Namespace = "MyNS",
                    Type = "MyType",
                    FilePath = "file.cs",
                    MaintainabilityIndex = 80,
                    CyclomaticComplexity = 2,
                    DepthOfInheritance = 1,
                    ClassCoupling = 3,
                    LinesOfSource = 50,
                    LinesOfExecutable = 30
                }
            };
            var members = new List<MemberMetrics>
            {
                new()
                {
                    Project = "MyProj",
                    Namespace = "MyNS",
                    Type = "MyType",
                    Member = "MyMethod()",
                    MaintainabilityIndex = 75,
                    CyclomaticComplexity = 4,
                    LinesOfSource = 20,
                    LinesOfExecutable = 15
                }
            };

            await CsvWriter.WriteAsync(tempFile, types, members);

            var lines = await File.ReadAllLinesAsync(tempFile);
            // Line 0=header, Line 1=type, Line 2=member
            lines[2].Should().Be("Member,MyProj,MyNS,MyType,MyMethod(),75,4,0,0,20,15");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WriteAsync_MemberRow_DoiAndClassCouplingAreZero()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var types = new List<TypeMetrics>
            {
                new() { Project = "P", Namespace = "N", Type = "T", FilePath = "f.cs" }
            };
            var members = new List<MemberMetrics>
            {
                new()
                {
                    Project = "P",
                    Namespace = "N",
                    Type = "T",
                    Member = "DoWork()",
                    MaintainabilityIndex = 90,
                    CyclomaticComplexity = 1,
                    LinesOfSource = 5,
                    LinesOfExecutable = 3
                }
            };

            await CsvWriter.WriteAsync(tempFile, types, members);

            var lines = await File.ReadAllLinesAsync(tempFile);
            var memberLine = lines[2];
            var cols = memberLine.Split(',');
            // cols: Scope, Project, Namespace, Type, Member, MI, CC, DOI, ClassCoupling, LoS, LoE
            cols[7].Should().Be("0", "DOI should be 0 for member rows");
            cols[8].Should().Be("0", "ClassCoupling should be 0 for member rows");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WriteAsync_RowsOrderedByProjectNamespaceTypeMember()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var types = new List<TypeMetrics>
            {
                new() { Project = "ProjectB", Namespace = "NS1", Type = "TypeA", FilePath = "f.cs" },
                new() { Project = "ProjectA", Namespace = "NS2", Type = "TypeC", FilePath = "f.cs" },
                new() { Project = "ProjectA", Namespace = "NS1", Type = "TypeB", FilePath = "f.cs" },
            };
            var members = new List<MemberMetrics>
            {
                new() { Project = "ProjectA", Namespace = "NS1", Type = "TypeB", Member = "MethodZ()", LinesOfSource = 1, LinesOfExecutable = 1 },
                new() { Project = "ProjectA", Namespace = "NS1", Type = "TypeB", Member = "MethodA()", LinesOfSource = 1, LinesOfExecutable = 1 },
            };

            await CsvWriter.WriteAsync(tempFile, types, members);

            var lines = await File.ReadAllLinesAsync(tempFile);
            // Skip header (line 0)
            // Expected order: ProjectA/NS1/TypeB, then ProjectA/NS2/TypeC, then ProjectB/NS1/TypeA
            lines[1].Should().Contain("ProjectA").And.Contain("NS1").And.Contain("TypeB");
            lines[2].Should().Contain("MethodA()");   // first member of TypeB
            lines[3].Should().Contain("MethodZ()");   // second member of TypeB
            lines[4].Should().Contain("ProjectA").And.Contain("NS2").And.Contain("TypeC");
            lines[5].Should().Contain("ProjectB").And.Contain("NS1").And.Contain("TypeA");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WriteAsync_FieldWithComma_IsEscapedWithQuotes()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var types = new List<TypeMetrics>
            {
                new() { Project = "My,Project", Namespace = "NS", Type = "T", FilePath = "f.cs" }
            };
            var members = new List<MemberMetrics>();

            await CsvWriter.WriteAsync(tempFile, types, members);

            var lines = await File.ReadAllLinesAsync(tempFile);
            lines[1].Should().Contain("\"My,Project\"");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WriteAsync_EmptyCollections_WritesOnlyHeader()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await CsvWriter.WriteAsync(tempFile, new List<TypeMetrics>(), new List<MemberMetrics>());

            var lines = await File.ReadAllLinesAsync(tempFile);
            lines.Should().HaveCount(1);
            lines[0].Should().Be(ExpectedHeader);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
