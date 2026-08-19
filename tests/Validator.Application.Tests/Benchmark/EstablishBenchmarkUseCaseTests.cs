using System;
using System.IO;
using System.Threading.Tasks;
using Validator.Application.Benchmark;
using Xunit;

namespace Validator.Application.Tests.Benchmark
{
    public class EstablishBenchmarkUseCaseTests
    {
        [Fact]
        public void BenchmarkName_DerivesSafeName()
        {
            var name = new BenchmarkName("My Benchmark!");
            Assert.Equal("my-benchmark", name.Safe);
        }

        [Fact]
        public void BenchmarkName_RejectsPathSeparators()
        {
            Assert.Throws<ArgumentException>(() => new BenchmarkName("test/../../../etc/passwd"));
        }

        [Fact]
        public void BenchmarkName_Equality()
        {
            var a = new BenchmarkName("Test Benchmark");
            var b = new BenchmarkName("Test Benchmark");
            Assert.Equal(a, b);
        }

        [Fact]
        public void BenchmarkName_ImplicitStringConversion()
        {
            var name = new BenchmarkName("My Benchmark");
            string safe = name;
            Assert.Equal("my-benchmark", safe);
        }

        [Fact]
        public void BenchmarkName_CollapsesMultipleHyphens()
        {
            var name = new BenchmarkName("My   Benchmark   Test");
            Assert.Equal("my-benchmark-test", name.Safe);
        }

        [Fact]
        public void BenchmarkName_RemovesSpecialCharacters()
        {
            var name = new BenchmarkName("AUDUSD D1 Benchmark!");
            Assert.Equal("audusd-d1-benchmark", name.Safe);
        }
    }
}
