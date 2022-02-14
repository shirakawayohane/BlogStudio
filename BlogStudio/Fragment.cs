using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlogStudio;


public partial class Program
{
    static async Task HandleFragmentChanged(Fragment fragment)
    {
        if(Fragments.TryGetValue(fragment, out var oldFragment))
        {
            oldFragment.Content = fragment.Content;
        } else
        {
            Fragments.Add(fragment);
        }

        await Task.WhenAll(Layouts.Where(layout => layout.FragmentDependencies.Contains(fragment.Name)).Select(async layout =>
        {
            await HandleLayoutChange(layout);
        }));
    }
    static async Task<Fragment> ReadFragment(string fullPath)
    {
        var name = Path.GetFileNameWithoutExtension(fullPath);
        var content = await File.ReadAllTextAsync(fullPath);
        return new Fragment(name, content);
    }
}

public class Fragment : IEquatable<Fragment>
{
    public Fragment(string name, string content)
    {
        Name = name;
        Content = content;
    }

    public string Name { get; }
    public string Content { get; set; }
    public override bool Equals(object? obj) => obj is Layout fragment && Equals(fragment);
    public bool Equals(Fragment? other) => other != null && other.Name == Name;
    public override int GetHashCode() => Name.GetHashCode();


}