using System;
using System.IO;
using System.Security.Cryptography;
using EDNexus.Core.Settings;
using Xunit;

namespace EDNexus.Tests
{
    public class AutoUpdateTests
    {
        [Fact]
        public void ComputeSha256Hex_ReturnsExpectedValue()
        {
            var temp = Path.GetTempFileName();
            try
            {
                var data = System.Text.Encoding.UTF8.GetBytes("hello world\n");
                File.WriteAllBytes(temp, data);

                // compute expected via framework
                using var fs = File.OpenRead(temp);
                using var sha = SHA256.Create();
                var expectedHash = sha.ComputeHash(fs);
                var expected = BitConverter.ToString(expectedHash).Replace("-", string.Empty).ToLowerInvariant();

                var actual = Hashing.ComputeSha256Hex(temp);
                Assert.Equal(expected, actual);
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }
}
