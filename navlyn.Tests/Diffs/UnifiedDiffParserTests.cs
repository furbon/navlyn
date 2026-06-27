using Navlyn.Diffs;

namespace Navlyn.Tests.Diffs;

public sealed class UnifiedDiffParserTests
{
    [Fact]
    public void Parse_ModifiedFile_ReturnsHunks()
    {
        const string diff = """
diff --git a/src/Widget.cs b/src/Widget.cs
index 1111111..2222222 100644
--- a/src/Widget.cs
+++ b/src/Widget.cs
@@ -10,2 +10,3 @@ public class Widget
+        Added();
""";

        DiffSet result = Parse(diff);

        DiffFile file = Assert.Single(result.Files);
        Assert.Equal("src/Widget.cs", file.Path);
        Assert.Equal("modified", file.Status);
        DiffHunk hunk = Assert.Single(file.Hunks);
        Assert.Equal(10, hunk.OldStart);
        Assert.Equal(2, hunk.OldLineCount);
        Assert.Equal(10, hunk.NewStart);
        Assert.Equal(3, hunk.NewLineCount);
    }

    [Fact]
    public void Parse_AddedFile_ReturnsAddedStatus()
    {
        const string diff = """
diff --git a/src/NewWidget.cs b/src/NewWidget.cs
new file mode 100644
index 0000000..2222222
--- /dev/null
+++ b/src/NewWidget.cs
@@ -0,0 +1,2 @@
+namespace Example;
+public sealed class NewWidget;
""";

        DiffFile file = Assert.Single(Parse(diff).Files);

        Assert.Equal("src/NewWidget.cs", file.Path);
        Assert.Equal("added", file.Status);
        Assert.Null(file.OldPath);
    }

    [Fact]
    public void Parse_DeletedFile_ReturnsDeletedStatusAndOldPath()
    {
        const string diff = """
diff --git a/src/OldWidget.cs b/src/OldWidget.cs
deleted file mode 100644
index 1111111..0000000
--- a/src/OldWidget.cs
+++ /dev/null
@@ -1,2 +0,0 @@
-namespace Example;
-public sealed class OldWidget;
""";

        DiffFile file = Assert.Single(Parse(diff).Files);

        Assert.Equal("src/OldWidget.cs", file.Path);
        Assert.Equal("deleted", file.Status);
        Assert.Null(file.OldPath);
        Assert.Equal(0, Assert.Single(file.Hunks).NewLineCount);
    }

    [Fact]
    public void Parse_RenamedFile_ReturnsOldPath()
    {
        const string diff = """
diff --git a/src/OldName.cs b/src/NewName.cs
similarity index 80%
rename from src/OldName.cs
rename to src/NewName.cs
--- a/src/OldName.cs
+++ b/src/NewName.cs
@@ -3 +3 @@ public class Widget
-    public void Old();
+    public void New();
""";

        DiffFile file = Assert.Single(Parse(diff).Files);

        Assert.Equal("src/NewName.cs", file.Path);
        Assert.Equal("src/OldName.cs", file.OldPath);
        Assert.Equal("renamed", file.Status);
    }

    [Fact]
    public void Parse_PathWithSpaces_NormalizesQuotedPath()
    {
        const string diff = """
diff --git "a/src/My Widget.cs" "b/src/My Widget.cs"
index 1111111..2222222 100644
--- "a/src/My Widget.cs"
+++ "b/src/My Widget.cs"
@@ -1 +1 @@
-old
+new
""";

        DiffFile file = Assert.Single(Parse(diff).Files);

        Assert.Equal("src/My Widget.cs", file.Path);
    }

    private static DiffSet Parse(string diff)
    {
        DiffReadResult result = new UnifiedDiffParser().Parse(
            diff,
            new DiffRequest("workingTree", Base: null, Head: null, Staged: false, IncludeUnstaged: true));

        Assert.Null(result.Error);
        return result.Diff!;
    }
}
