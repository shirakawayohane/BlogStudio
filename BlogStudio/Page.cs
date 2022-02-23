using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlogStudio
{
    public partial class Program
    {
        public async Task<Page> ReadPage(string fullPath)
        {
            var path = fullPath[(PagePath.Length + 1)..];
            var fileContent = await File.ReadAllTextAsync(fullPath);
            var ret = Helpers.YamlDeserializer.Deserialize<Page>(fullPath);
        }
    }
    public record Page(string Path, string Layout, string Content, string? Variant);
}
