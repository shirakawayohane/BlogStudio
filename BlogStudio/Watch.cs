using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlogStudio
{
    public partial class Program
    {
        static IDisposable Watch()
        {
            var disposableBag = new DisposableBag();
            var throttleTime = TimeSpan.FromMilliseconds(ThrottleMs);
            var postsWatcher = new FileSystemWatcher(PostPath);
            postsWatcher.ChangedAsObservable().Throttle(throttleTime).Subscribe(static async e =>
            {
                if (e.ChangeType is not WatcherChangeTypes.Changed) return;
                var post = await ReadPost(e.FullPath);
                if (post != null) await HandlePostChange(post);
            });

            postsWatcher.RenamedAsObservable().Throttle(throttleTime).Subscribe(static async e =>
            {
                var (oldOutputPath, _, _, _) = ParsePostPath(e.OldFullPath);

                Posts.RemoveWhere(post => post.OutputPath == oldOutputPath);
                if (File.Exists(oldOutputPath)) File.Delete(oldOutputPath);

                await HandlePostChange(await ReadPost(e.FullPath));
            });

            // do not watch Created Events because newly created file is not valid as a post in almost cases.

            postsWatcher.DeletedAsObservable().Throttle(throttleTime).Subscribe(e =>
            {
                var (outPutPath, _, _, _) = ParsePostPath(e.FullPath);
                Posts.RemoveWhere(post => post.OutputPath == outPutPath);
                if (File.Exists(outPutPath)) File.Delete(outPutPath);
            });
            postsWatcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastWrite;
            postsWatcher.IncludeSubdirectories = true;
            postsWatcher.EnableRaisingEvents = true;
            disposableBag.AddTo(postsWatcher);

            var fragmentsWatcher = new FileSystemWatcher(FragmentPath);

            fragmentsWatcher.ChangedAsObservable().Throttle(throttleTime).Subscribe(static async e =>
            {
                var fragment = await ReadFragment(e.FullPath);
                await HandleFragmentChanged(fragment);
            });
            fragmentsWatcher.DeletedAsObservable().Throttle(throttleTime).Subscribe(static async e =>
            {
                var fragmentName = Path.GetFileNameWithoutExtension(e.FullPath);
                Fragments.RemoveWhere(x => x.Name == fragmentName);
                await Parallel.ForEachAsync(
                    Layouts.Where(layout => layout.FragmentDependencies.Contains(fragmentName)),
                    async (layout, token) =>
                    {
                        await HandleLayoutChange(layout);
                    }
                );
            });

            fragmentsWatcher.Error += static (s, e) =>
            {
                Console.WriteLine("Error");
            };

            fragmentsWatcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastWrite;
            fragmentsWatcher.IncludeSubdirectories = true;
            fragmentsWatcher.EnableRaisingEvents = true;
            disposableBag.AddTo(fragmentsWatcher);


            var layoutsWatcher = new FileSystemWatcher(LayoutPath);

            layoutsWatcher.ChangedAsObservable().Throttle(throttleTime).Subscribe(static async e =>
            {
                var layout = await ReadLayout(e.FullPath);
                await HandleLayoutChange(layout);
            });

            layoutsWatcher.DeletedAsObservable().Throttle(throttleTime).Subscribe(static async e =>
            {
                var layoutName = Path.GetFileNameWithoutExtension(e.FullPath);
                Layouts.RemoveWhere(x => x.Name == layoutName);
                await Parallel.ForEachAsync(
                    Posts.Where(post => post.Layout == layoutName),
                    async (post, token) =>
                    {
                        await HandlePostChange(post);
                    }
                );
            });
            layoutsWatcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastWrite;

            layoutsWatcher.Filter = "*.html";
            layoutsWatcher.EnableRaisingEvents = true;
            disposableBag.AddTo(layoutsWatcher);

            var assetsWatcher = new FileSystemWatcher(AssetPath);
            assetsWatcher.ChangedAsObservable().Throttle(throttleTime).Subscribe(static asset =>
            {
                File.Copy(asset.FullPath, Path.Combine(OutDir, asset.FullPath.Substring(AssetPath.Length + 1)), true);
            });
            assetsWatcher.DeletedAsObservable().Throttle(throttleTime).Subscribe(static asset =>
            {
                File.Delete(Path.Combine(OutDir, asset.FullPath.Substring(AssetPath.Length + 1)));
            });
            assetsWatcher.CreatedAsObservable().Throttle(throttleTime).Subscribe(static asset =>
            {
                File.Copy(asset.FullPath, Path.Combine(OutDir, asset.FullPath.Substring(AssetPath.Length + 1)));
            });
            assetsWatcher.RenamedAsObservable().Throttle(throttleTime).Subscribe(static asset =>
            {
                File.Move(Path.Combine(OutDir, asset.OldFullPath.Substring(AssetPath.Length + 1)), Path.Combine(OutDir, asset.FullPath.Substring(8)), true);
            });
            assetsWatcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastWrite;
            assetsWatcher.EnableRaisingEvents = true;
            disposableBag.AddTo(assetsWatcher);

            return disposableBag;
        }
    }

    internal class DisposableBag : IDisposable
    {
        List<IDisposable> _disposables = new();
        public DisposableBag(params IDisposable[] disposables)
        {
            _disposables = disposables.ToList();
        }

        public void AddTo(IDisposable disposable)
        {
            _disposables.Add(disposable);
        }

        public void Dispose()
        {
            foreach(var disposable in _disposables)
                disposable.Dispose();
        }
    }
}
