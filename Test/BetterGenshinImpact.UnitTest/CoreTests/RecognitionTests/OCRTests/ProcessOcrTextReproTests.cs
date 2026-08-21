using System.Reflection;
using BetterGenshinImpact.GameTask.AutoPick;
using Xunit;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests
{
    /// <summary>
    /// Regression tests for the bug "auto-pick silently does nothing on non-Chinese clients
    /// (EN/FR/IT/...)": AutoPickTrigger.ProcessOcrText only kept characters inside the CJK
    /// range (0x4E00-0x9FFF) or the 「」 quote marks, so any purely Latin OCR text
    /// (e.g. "Ciufferba", "Katheryne", "Selezione rapida") was trimmed down to "" and the
    /// pick trigger aborted right after, silently (no log, no F key press).
    /// With the Yap engine the symptom was masked: OCR (tuned for Chinese) run on non-Chinese
    /// text produces random hanzi that survive the CJK filter, giving the illusion that
    /// something was read/logged even though it's garbage.
    /// </summary>
    public class ProcessOcrTextReproTests
    {
        private static string Invoke(string text)
        {
            var mi = typeof(AutoPickTrigger).GetMethod("ProcessOcrText", BindingFlags.NonPublic | BindingFlags.Static)!;
            return (string)mi.Invoke(null, new object[] { text })!;
        }

        [Theory]
        [InlineData("Ciufferba", "Ciufferba")]
        [InlineData("Pesca", "Pesca")]
        // Step 0 (pre-existing, untouched by this fix) strips all whitespace:
        // unchanged behavior, asserted here explicitly.
        [InlineData("Selezione rapida", "Selezionerapida")]
        [InlineData("Katheryne", "Katheryne")]
        [InlineData("Set Includes", "SetIncludes")]
        public void LatinOnlyOcrText_IsPreserved(string input, string expected)
        {
            // Before the fix: every one of these inputs became "" (regardless of whitespace).
            Assert.Equal(expected, Invoke(input));
        }

        [Theory]
        [InlineData("烹饪", "烹饪")]
        [InlineData("凯瑟琳", "凯瑟琳")]
        public void ChineseOcrText_StillWorks(string input, string expected)
        {
            Assert.Equal(expected, Invoke(input));
        }

        [Fact]
        public void GarbageHanziMixedWithNoise_StillPartiallySurvives()
        {
            // Pre-existing behavior for the Yap-engine branch, unchanged by this fix.
            Assert.Equal("口区烹", Invoke(" 口区烹  "));
        }

        [Fact]
        public void BracketedChineseText_StillBalancesQuotes()
        {
            Assert.Equal("「香辛料」", Invoke("香辛料」"));
        }
    }
}
