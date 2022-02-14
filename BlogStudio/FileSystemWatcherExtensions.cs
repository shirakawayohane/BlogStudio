using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlogStudio;
internal static class FileSystemWatcherExtensions
{
    public static IObservable<FileSystemEventArgs> ChangedAsObservable(this FileSystemWatcher self)
    {
        return Observable.FromEvent<FileSystemEventHandler, FileSystemEventArgs>(
            h => (_, e) => h(e),
            h => self.Changed += h,
            h => self.Changed -= h);
    }

    public static IObservable<RenamedEventArgs> RenamedAsObservable(this FileSystemWatcher self)
    {
        return Observable.FromEvent<RenamedEventHandler, RenamedEventArgs>(
            h => (_, e) => h(e),
            h => self.Renamed += h,
            h => self.Renamed -= h);
    }

    public static IObservable<FileSystemEventArgs> CreatedAsObservable(this FileSystemWatcher self)
    {
        return Observable.FromEvent<FileSystemEventHandler, FileSystemEventArgs>(
            h => (_, e) => h(e),
            h => self.Created += h,
            h => self.Created -= h);
    }

    public static IObservable<FileSystemEventArgs> DeletedAsObservable(this FileSystemWatcher self)
    {
        return Observable.FromEvent<FileSystemEventHandler, FileSystemEventArgs>(
            h => (_, e) => h(e),
            h => self.Deleted += h,
            h => self.Deleted -= h);
    }

}