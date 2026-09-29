using System;
using Shared.Math;
using Xunit;

namespace Shared.Tests
{
    public class Vector3Tests
    {
        [Fact]
        public void Distance_ShouldReturnZero_WhenVectorsAreIdentical()
        {
            var a = new Vector3(10f, 20f, 30f);
            var b = new Vector3(10f, 20f, 30f);
            
            float distance = Vector3.Distance(a, b);
            
            Assert.Equal(0f, distance);
        }

        [Fact]
        public void Distance_ShouldCalculateCorrectly_In2D()
        {
            var a = new Vector3(0f, 0f, 0f);
            var b = new Vector3(3f, 4f, 0f); // 3-4-5 right triangle
            
            float distance = Vector3.Distance(a, b);
            
            Assert.Equal(5f, distance);
        }

        [Fact]
        public void Distance_ShouldCalculateCorrectly_In3D()
        {
            var a = new Vector3(0f, 0f, 0f);
            var b = new Vector3(10f, 10f, 10f);
            
            float distance = Vector3.Distance(a, b);
            
            // sqrt(10^2 + 10^2 + 10^2) = sqrt(300) = 17.3205081f
            Assert.True(System.Math.Abs(17.3205081f - distance) < 0.0001f);
        }
    }
}
