using System.Reflection;
using BetterGenshinImpact.GameTask.AutoPick;
using Xunit;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests
{
    /// <summary>
    /// Regressione per il bug "raccolta con OCR engine Paddle non logga/raccoglie nulla su
    /// client non-cinesi (EN/FR/IT/...)": AutoPickTrigger.ProcessOcrText teneva solo i caratteri
    /// nel range CJK (0x4E00-0x9FFF) o le virgolette 「」, quindi qualunque testo OCR puramente
    /// latino (es. "Ciufferba", "Katheryne", "Selezione rapida") veniva svuotato a "" e il
    /// trigger abortiva silenziosamente subito dopo (nessun log, nessuna pressione del tasto F).
    /// Col motore Yap, invece, l'OCR (tarato sul cinese) prodotto su testo non-cinese generava
    /// hanzi casuali che sopravvivevano al filtro CJK, dando l'illusione che "qualcosa" venisse
    /// letto/loggato pur essendo spazzatura.
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
        // Lo step 0 (pre-esistente, non toccato da questo fix) rimuove tutti gli spazi:
        // comportamento invariato, qui atteso esplicitamente.
        [InlineData("Selezione rapida", "Selezionerapida")]
        [InlineData("Katheryne", "Katheryne")]
        [InlineData("Set Includes", "SetIncludes")]
        public void LatinOnlyOcrText_IsPreserved(string input, string expected)
        {
            // Prima del fix: tutti questi input diventavano "" (a prescindere dagli spazi).
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
            // Comportamento pre-esistente e invariato per il ramo Yap.
            Assert.Equal("口区烹", Invoke(" 口区烹  "));
        }

        [Fact]
        public void BracketedChineseText_StillBalancesQuotes()
        {
            Assert.Equal("「香辛料」", Invoke("香辛料」"));
        }
    }
}
