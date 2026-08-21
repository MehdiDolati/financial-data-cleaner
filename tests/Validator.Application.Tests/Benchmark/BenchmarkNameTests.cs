using System;
using Validator.Application.Benchmark;
using Xunit;

namespace Validator.Application.Tests.Benchmark
{
    public class BenchmarkNameTests
    {
        [Theory]
        [InlineData("My Benchmark!", "my-benchmark")]
        [InlineData("Test  Benchmark", "test-benchmark")]
        [InlineData("AUDUSD D1 Daily", "audusd-d1-daily")]
        [InlineData("HELLO", "hello")]
        [InlineData("a", "a")]
        [InlineData("test-name-123", "test-name-123")]
        public void Constructor_DerivesSafeName(string input, string expected)
        {
            var name = new BenchmarkName(input);
            Assert.Equal(expected, name.Safe);
        }

        [Theory]
        [InlineData("test/attack")]
        [InlineData("test\\attack")]
        [InlineData("test:attack")]
        public void Constructor_RejectsPathSeparators(string input)
        {
            Assert.Throws<ArgumentException>(() => new BenchmarkName(input));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_RejectsEmptyName(string input)
        {
            Assert.Throws<ArgumentException>(() => new BenchmarkName(input));
        }

        [Fact]
        public void Constructor_RejectsNameProducingEmptySafe()
        {
            Assert.Throws<ArgumentException>(() => new BenchmarkName("!!!"));
        }

        [Fact]
        public void Raw_IsPreserved()
        {
            var name = new BenchmarkName("My Benchmark!");
            Assert.Equal("My Benchmark!", name.Raw);
        }

        [Fact]
        public void Safe_IsLowercase()
        {
            var name = new BenchmarkName("UPPERCASE");
            Assert.Equal("uppercase", name.Safe);
        }

        [Fact]
        public void Safe_CollapsesMultipleHyphens()
        {
            var name = new BenchmarkName("a---b");
            Assert.Equal("a-b", name.Safe);
        }

        [Fact]
        public void Safe_TrimsLeadingTrailingHyphens()
        {
            var name = new BenchmarkName("-test-");
            Assert.Equal("test", name.Safe);
        }

        [Fact]
        public void Equality_SameNamesAreEqual()
        {
            var a = new BenchmarkName("Test");
            var b = new BenchmarkName("Test");
            Assert.Equal(a, b);
            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Equality_DifferentNamesAreNotEqual()
        {
            var a = new BenchmarkName("Test1");
            var b = new BenchmarkName("Test2");
            Assert.NotEqual(a, b);
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equality_CaseInsensitive()
        {
            var a = new BenchmarkName("Test");
            var b = new BenchmarkName("test");
            Assert.Equal(a, b);
        }

        [Fact]
        public void GetHashCode_EqualForEqualNames()
        {
            var a = new BenchmarkName("Test");
            var b = new BenchmarkName("Test");
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void ToString_ReturnsSafeName()
        {
            var name = new BenchmarkName("My Benchmark!");
            Assert.Equal("my-benchmark", name.ToString());
        }

        [Fact]
        public void ImplicitStringConversion_ReturnsSafeName()
        {
            var name = new BenchmarkName("My Benchmark!");
            string safe = name;
            Assert.Equal("my-benchmark", safe);
        }

        [Fact]
        public void Equals_Object_ReturnsTrueForEqual()
        {
            var name = new BenchmarkName("Test");
            object obj = new BenchmarkName("Test");
            Assert.True(name.Equals(obj));
        }

        [Fact]
        public void Equals_Object_ReturnsFalseForDifferent()
        {
            var name = new BenchmarkName("Test");
            object obj = new BenchmarkName("Other");
            Assert.False(name.Equals(obj));
        }

        [Fact]
        public void Equals_Object_ReturnsFalseForNull()
        {
            var name = new BenchmarkName("Test");
            Assert.False(name.Equals(null));
        }

        [Fact]
        public void Equals_Object_ReturnsFalseForNonBenchmarkName()
        {
            var name = new BenchmarkName("Test");
            Assert.False(name.Equals("Test"));
        }
    }
}
