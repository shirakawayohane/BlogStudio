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
            var throttleTime = TimeSpan.FromMilliseconds(ThrottleMs);
            var postsWatcher = new FileSystemWatcher(PostPath);
            postsWatcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastWrite;

            postsWatcher.ChangedAsObservable().Throttle(throttleTime).Subscribe(async e =>
            {
                if (e.ChangeType is not WatcherChangeTypes.Changed) return;
                var post = await ReadPost(e.FullPath);
                if (post != null) await HandlePostChange(post);
            });

            postsWatcher.RenamedAsObservable().Throttle(throttleTime).Subscribe(async e =>
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

            postsWatcher.Filter = "*.md|*.html";
            postsWatcher.EnableRaisingEvents = true;

            var fragmentsWatcher = new FileSystemWatcher(FragmentPath);
            fragmentsWatcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastWrite;
            fragmentsWatcher.Filter = "*.html";
            fragmentsWatcher.ChangedAsObservable().Throttle(throttleTime).Subscribe(async e =>
            {
                var fragment = await ReadFragment(e.FullPath);
                await HandleFragmentChanged(fragment);
            });
            fragmentsWatcher.DeletedAsObservable().Throttle(throttleTime).Subscribe(async e =>
            {
                var fragmentName = Path.GetFileNameWithoutExtension(e.FullPath);
                Fragments.RemoveWhere(x => x.Name == fragmentName);
                await Parallel.ForEachAsync(Layouts.Where(layout => layout.FragmentDependencies.Contains(fragmentName)), async (layout, token) =>
                {
                    await HandleLayoutChange(layout);
                });
            });
            fragmentsWatcher.EnableRaisingEvents = true;


            var layoutsWatcher = new FileSystemWatcher(LayoutPath);
            layoutsWatcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastWrite;

            layoutsWatcher.Filter = "*.html";

            layoutsWatcher.ChangedAsObservable().Throttle(throttleTime).Subscribe(async e =>
            {
                var layout = await ReadLayout(e.FullPath);
                await HandleLayoutChange(layout);
            });

            layoutsWatcher.DeletedAsObservable().Throttle(throttleTime).Subscribe(async e =>
            {
                var layoutName = Path.GetFileNameWithoutExtension(e.FullPath);
                Layouts.RemoveWhere(x => x.Name == layoutName);
                await Parallel.ForEachAsync(Posts.Where(post => post.Layout == layoutName), async (post, token) =>
                {
                    await HandlePostChange(post);
                });
            });
            layoutsWatcher.EnableRaisingEvents = true;

            var assetsWatcher = new FileSystemWatcher(AssetPath);
            assetsWatcher.ChangedAsObservable().Throttle(throttleTime).Subscribe(asset =>
            {
                File.Copy(asset.FullPath, Path.Combine(OutDir, asset.FullPath.Substring(AssetPath.Length + 1)), true);
            });
            assetsWatcher.DeletedAsObservable().Throttle(throttleTime).Subscribe(asset =>
            {
                File.Delete(Path.Combine(OutDir, asset.FullPath.Substring(AssetPath.Length + 1)));
            });
            assetsWatcher.CreatedAsObservable().Throttle(throttleTime).Subscribe(asset =>
            {
                File.Copy(asset.FullPath, Path.Combine(OutDir, asset.FullPath.Substring(AssetPath.Length + 1)));
            });
            assetsWatcher.RenamedAsObservable().Throttle(throttleTime).Subscribe(asset =>
            {
                File.Move(Path.Combine(OutDir, asset.OldFullPath.Substring(AssetPath.Length + 1)), Path.Combine(OutDir, asset.FullPath.Substring(8)), true);
            });
            assetsWatcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastWrite;
            assetsWatcher.EnableRaisingEvents = true;

            return new DisposableBag(postsWatcher, layoutsWatcher, assetsWatcher);
        }
    }

    internal class DisposableBag : IDisposable
    {
        IDisposable[] _disposables;
        public DisposableBag(params IDisposable[] disposables)
        {
            _disposables = disposables;
        }
        public void Dispose()
        {
            foreach(var disposable in _disposables)
                disposable.Dispose();
        }
    }
}
