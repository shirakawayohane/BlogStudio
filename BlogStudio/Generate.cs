using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlogStudio
{
    public partial class Program
    {
        static async Task GenerateAsync()
        {
            Directory.CreateDirectory(OutDir);

            await Parallel.ForEachAsync(Directory.EnumerateFiles(PostPath), async (postPath, token) =>
            {
                if (!postPath.EndsWith(".md") && !postPath.EndsWith(".html")) return;

                var post = await ReadPost(postPath, true);
                if (post != null) Posts.Add(post);
                // generate after layouts loaded.
            });

            await Parallel.ForEachAsync(Directory.EnumerateFiles(FragmentPath), async (fragmentPath, token) =>
            {
                var fragment = await ReadFragment(fragmentPath);
                if (fragment != null) Fragments.Add(fragment);
            });

            await Parallel.ForEachAsync(Directory.EnumerateFiles(LayoutPath), async (layoutPath, token) =>
            {
                var layout = await ReadLayout(layoutPath);
                Layouts.Add(layout);
            });
            // Add empty layout.
            var emptyLayout = new Layout(EmptyLayout, "{{content}}", new());
            Layouts.Add(emptyLayout);

            await Parallel.ForEachAsync(Layouts, async (layout, token) =>
            {
                await HandleLayoutChange(layout);
            });

            // Copy asset files.
            foreach (var assetPath in Directory.EnumerateFiles(AssetPath))
            {
                File.Copy(assetPath, Path.Combine(OutDir, assetPath[(AssetPath.Length + 1)..]), true);
            }

        }
    }
}
