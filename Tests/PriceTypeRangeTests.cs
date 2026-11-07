using System;
using MGAutoSell.Query;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimWorld;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PriceTypeRange"/> verifying range bounds, equality, inclusion, and string parsing.
    /// </summary>
    [TestClass]
    public class PriceTypeRangeTests
    {
        [TestMethod]
        public void Constructor_UndefinedBounds_NormalizesToVeryCheapAndExorbitant()
        {
            var range = new PriceTypeRange(PriceType.Undefined, PriceType.Undefined);

            Assert.AreEqual(PriceType.VeryCheap, range.min);
            Assert.AreEqual(PriceType.Exorbitant, range.max);
            Assert.AreEqual(PriceTypeRange.All, range);
        }

        [TestMethod]
        public void Includes_EvaluatesBoundsCorrectlyAndExcludesUndefined()
        {
            var range = new PriceTypeRange(PriceType.Cheap, PriceType.Expensive);

            Assert.IsFalse(range.Includes(PriceType.Undefined));
            Assert.IsFalse(range.Includes(PriceType.VeryCheap));
            Assert.IsTrue(range.Includes(PriceType.Cheap));
            Assert.IsTrue(range.Includes(PriceType.Normal));
            Assert.IsTrue(range.Includes(PriceType.Expensive));
            Assert.IsFalse(range.Includes(PriceType.Exorbitant));
        }

        [TestMethod]
        public void FromString_And_ToString_RoundTripsAndHandlesCaseAndWhitespace()
        {
            var original = new PriceTypeRange(PriceType.Cheap, PriceType.Expensive);
            string serialized = original.ToString();

            Assert.AreEqual("Cheap~Expensive", serialized);

            var parsed = PriceTypeRange.FromString(serialized);
            Assert.AreEqual(original, parsed);
            Assert.IsTrue(original.Equals((object)parsed));
            Assert.IsFalse(original.Equals("not a range"));
            Assert.IsTrue(original == parsed);
            Assert.IsFalse(original != parsed);
            Assert.AreEqual(original.GetHashCode(), parsed.GetHashCode());

            var whitespaceParsed = PriceTypeRange.FromString("  cheap ~ expensive  ");
            Assert.AreEqual(original, whitespaceParsed);
        }

        [TestMethod]
        public void FromString_InvalidFormat_ThrowsFormatException()
        {
            Assert.ThrowsException<FormatException>(() => PriceTypeRange.FromString(null));
            Assert.ThrowsException<FormatException>(() => PriceTypeRange.FromString(""));
            Assert.ThrowsException<FormatException>(() => PriceTypeRange.FromString("   "));
            Assert.ThrowsException<FormatException>(() => PriceTypeRange.FromString("Normal"));
            Assert.ThrowsException<FormatException>(() => PriceTypeRange.FromString("~Normal"));
            Assert.ThrowsException<FormatException>(() => PriceTypeRange.FromString("Normal~"));
        }
    }
}
