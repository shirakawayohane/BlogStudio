using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BlogStudio
{
    public partial class Program
    {
        static async Task<Layout> ReadLayout(string fullPath)
        {
            var name = Path.GetFileNameWithoutExtension(fullPath);
            var content = await File.ReadAllTextAsync(fullPath);
            HashSet<string> fragDeps = new HashSet<string>();
            foreach(Match match in FragmentRegex.Matches(content))
            {
                var fragmentName = match.Groups[1].Value!;
                fragDeps.Add(fragmentName);
            }
            return new Layout(name, content, fragDeps);
        }
        static async Task HandleLayoutChange(Layout layout)
        {
            // Update cache
            if (Layouts.TryGetValue(layout, out var oldLayout))
            {
                oldLayout.Content = layout.Content;
                oldLayout.FragmentDependencies = layout.FragmentDependencies;
            }
            else
            {
                Layouts.Add(layout);
            }

            await Parallel.ForEachAsync(Posts.Where(x => x.Layout == layout.Name), async (post, token) =>
            {
                await HandlePostChange(post);
            });

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"A Layout `{layout.Name}` has (re)loaded.");
            Console.ResetColor();
        }
    }

    public class Layout : IEquatable<Layout>
    {
        public Layout(string name, string content, HashSet<string> fragDeps)
        {
            Name = name;
            Content = content;
            FragmentDependencies = fragDeps;
        }

        public string Name { get; }
        public string Content { get; set; }
        public HashSet<string> FragmentDependencies { get; set; } = new();
        public override bool Equals(object? obj) => obj is Layout layout && Equals(layout);
        public bool Equals(Layout? other) => other != null && other.Name == Name;
        public override int GetHashCode() => Name.GetHashCode();
    }
}
