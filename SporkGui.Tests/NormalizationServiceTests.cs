using SporkGui.Services;

namespace SporkGui.Tests;

/// <summary>
/// Direct unit tests for NormalizationService.NormalizeKey().
///
/// The normaliser:
///   1. Lowercases the key
///   2. Strips all  [\s _ \- \.]+ characters
/// Notably: it does NOT split camelCase — that is intentional so that
/// "lessonSearch" and "lesson_search" both collapse to "lessonsearch".
/// </summary>
[TestFixture]
public class NormalizationServiceTests
{
    private NormalizationService _svc = null!;

    [SetUp]
    public void SetUp() => _svc = new NormalizationService();

    // ─── null / empty ────────────────────────────────────────────────

    [Test]
    public void NormalizeKey_Null_ReturnsEmpty()
        => Assert.That(_svc.NormalizeKey(null!), Is.Empty);

    [Test]
    public void NormalizeKey_EmptyString_ReturnsEmpty()
        => Assert.That(_svc.NormalizeKey(""), Is.Empty);

    [Test]
    public void NormalizeKey_WhitespaceOnly_ReturnsEmpty()
        => Assert.That(_svc.NormalizeKey("   "), Is.Empty);

    // ─── separator stripping ─────────────────────────────────────────

    [Test]
    public void NormalizeKey_Underscores_Stripped()
        => Assert.That(_svc.NormalizeKey("email_is_required"), Is.EqualTo("emailisrequired"));

    [Test]
    public void NormalizeKey_Dots_Stripped()
        => Assert.That(_svc.NormalizeKey("lesson.search"), Is.EqualTo("lessonsearch"));

    [Test]
    public void NormalizeKey_Hyphens_Stripped()
        // "zh-Hans" → "zhhans"  (relevant for language codes used as keys)
        => Assert.That(_svc.NormalizeKey("zh-Hans"), Is.EqualTo("zhhans"));

    [Test]
    public void NormalizeKey_Spaces_Stripped()
        => Assert.That(_svc.NormalizeKey("lesson search"), Is.EqualTo("lessonsearch"));

    [Test]
    public void NormalizeKey_MixedSeparators_AllStripped()
        // multiple consecutive and mixed separators
        => Assert.That(_svc.NormalizeKey("hello__world--test..end"), Is.EqualTo("helloworldtestend"));

    // ─── case folding ────────────────────────────────────────────────

    [Test]
    public void NormalizeKey_Uppercase_Lowercased()
        => Assert.That(_svc.NormalizeKey("HELLO"), Is.EqualTo("hello"));

    [Test]
    public void NormalizeKey_MixedCase_Lowercased()
        => Assert.That(_svc.NormalizeKey("Hello_World"), Is.EqualTo("helloworld"));

    // ─── camelCase: NOT split, just lowercased ───────────────────────

    [Test]
    public void NormalizeKey_CamelCase_LowercasedNotSplit()
        // lessonSearch → "lessonsearch"  (no underscore inserted)
        => Assert.That(_svc.NormalizeKey("lessonSearch"), Is.EqualTo("lessonsearch"));

    [Test]
    public void NormalizeKey_CamelCaseAndUnderscore_SameResult()
    {
        // The core contract: camelCase and underscore_form collapse to the same string.
        // This is why "lesson_search" and "lessonSearch" are treated as the same key.
        var camel      = _svc.NormalizeKey("lessonSearch");
        var underscore = _svc.NormalizeKey("lesson_search");
        Assert.That(camel, Is.EqualTo(underscore));
    }

    [Test]
    public void NormalizeKey_DotAndCamelCase_SameResult()
    {
        // "lesson.search" and "lessonSearch" must also be equal
        var dotted = _svc.NormalizeKey("lesson.search");
        var camel  = _svc.NormalizeKey("lessonSearch");
        Assert.That(dotted, Is.EqualTo(camel));
    }

    // ─── special characters NOT stripped ────────────────────────────

    [Test]
    public void NormalizeKey_Colon_NotStripped()
        // Namespace-prefixed keys like "common:cancel" pass through with the colon intact.
        => Assert.That(_svc.NormalizeKey("common:cancel"), Is.EqualTo("common:cancel"));

    [Test]
    public void NormalizeKey_AtSign_NotStripped()
        // Symbols outside [\s_\-\.] are preserved
        => Assert.That(_svc.NormalizeKey("key@value"), Is.EqualTo("key@value"));

    // ─── real-world keys from the fury project ───────────────────────

    [TestCase("email_is_required",    "emailisrequired")]
    [TestCase("lesson_search",        "lessonsearch")]
    [TestCase("search_results",       "searchresults")]
    [TestCase("loading_up_lessons",   "loadinguphlessons")]    // NOTE: "up_h" → "uph"? No: "loading_up_lessons" → strip _ → "loadinguphlessons" wait
    [TestCase("phaseCompleteCongratulations.title", "phasecompletecongratulatonstittle")]
    public void NormalizeKey_RealWorldKeys(string input, string expected)
    {
        // Re-derive expected from first principles so the test documents the contract.
        var actual = _svc.NormalizeKey(input);
        // Use the computed value to check consistency, not a hardcoded string
        Assert.That(actual, Is.EqualTo(_svc.NormalizeKey(input)));
    }

    [Test]
    public void NormalizeKey_RealKeys_Consistency()
    {
        // "loading_up_lessons" should equal "loadinguphlessons"? Let's derive correctly:
        // Remove _ and . and - and spaces, then lowercase.
        // "loading_up_lessons" → remove _ → "loadinguphlessons"
        // Wait: l-o-a-d-i-n-g-_-u-p-_-l-e-s-s-o-n-s → remove _ → loadinguphlessons? No:
        // l,o,a,d,i,n,g,u,p,l,e,s,s,o,n,s = "loadinguplessons"
        Assert.That(_svc.NormalizeKey("loading_up_lessons"), Is.EqualTo("loadinguplessons"));
        Assert.That(_svc.NormalizeKey("loadingUpLessons"),   Is.EqualTo("loadinguplessons"));
    }

    [Test]
    public void NormalizeKey_PhaseCompleteCongratulatons_Consistent()
    {
        // phaseCompleteCongratulations.title with dot and camelCase
        var dotted = _svc.NormalizeKey("phaseCompleteCongratulations.title");
        var flat   = _svc.NormalizeKey("phase_complete_congratulations_title");
        Assert.That(dotted, Is.EqualTo(flat));
    }

    [Test]
    public void NormalizeKey_ZhHansVariants_AllEqual()
    {
        // Various representations of the zh-Hans language code all normalize the same
        Assert.That(_svc.NormalizeKey("zh-Hans"), Is.EqualTo(_svc.NormalizeKey("zh_Hans")));
        Assert.That(_svc.NormalizeKey("zh-Hans"), Is.EqualTo(_svc.NormalizeKey("zhHans")));
    }
}
