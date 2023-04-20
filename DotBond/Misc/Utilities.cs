using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Xml.Serialization;

namespace DotBond.Misc;

public static class Utilities
{
    public static IEqualityComparer<TSource> CreateEqualityComparer<TSource>(Func<TSource, TSource, bool> compareExpression)
    {
        return new EqualityComparerDummy<TSource>(compareExpression);
    }

    public class EqualityComparerDummy<TSource> : IEqualityComparer<TSource>
    {
        public Func<TSource, TSource, bool> CompareExpression;

        public EqualityComparerDummy(Func<TSource, TSource, bool> compareExpression)
        {
            CompareExpression = compareExpression;
        }

        public bool Equals(TSource x, TSource y)
        {
            return CompareExpression.Invoke(x, y);
        }

        public int GetHashCode(TSource obj)
        {
            throw new NotImplementedException();
        }
    }


    public static List<string> GetExcludedFiles(string fullPath)
    {
        var serializer = new XmlSerializer(typeof(Project));
        Project project;
        using (Stream reader = new FileStream(fullPath, FileMode.Open))
            project = (Project)serializer.Deserialize(reader)!;

        return project.ItemGroup.Where(e => e.TestElement.Any()).SelectMany(e => e.TestElement.Select(ee => ee.Remove)).ToList();
    }


    [XmlRoot("Project")]
    public class Project
    {
        [XmlElement("ItemGroup")] public List<ItemGroup> ItemGroup { get; set; }
    }

    public class ItemGroup
    {
        [XmlElement("Compile")] public List<Compile> TestElement { get; set; }
    }

    public class Compile
    {
        [XmlText] public int Value { get; set; }

        [XmlAttribute] public string Remove { get; set; }
    }

    /// <summary>
    /// Combines outputs of the 
    /// </summary>
    /// <param name="first"></param>
    /// <param name="secondObservalbeSelector"></param>
    /// <typeparam name="TFirst"></typeparam>
    /// <typeparam name="TSecond"></typeparam>
    /// <returns></returns>
    public static IObservable<(TFirst First, TSecond Second)> ThrottleAndCombine<TFirst, TSecond>(this IObservable<TFirst> first, Func<TFirst, IObservable<TSecond>> secondObservalbeSelector)
    {
        IDisposable activeObservable = null;
        return Observable.Create<(TFirst First, TSecond Second)>(observer =>
        {
            return first.Subscribe(value =>
            {
                activeObservable?.Dispose();

                activeObservable = secondObservalbeSelector.Invoke(value).Retry(10).Catch(Observable.Empty<TSecond>()).Throttle(TimeSpan.FromMilliseconds(1))
                    .Subscribe(secondValue => observer.OnNext((value, secondValue)));
            }, observer.OnError, observer.OnCompleted);
        });
    }

    /// <summary>
    /// Continues the stream only after the provided Task from the previous emission has completed successfully, or has been cancelled.
    /// </summary>
    public static IObservable<(string FilePath, string FileContent)> AfterRead(this IObservable<string> first, Func<string, CancellationToken, Task<string>> taskToComplete)
    {
        Task<string> currentTask = null!;
        CancellationTokenSource previousTaskCancellationSource = null!;

        return first.Select(value =>
            {
                var taskToAwait = currentTask;
                previousTaskCancellationSource?.Cancel();
                previousTaskCancellationSource = new();
                // currentTask = ;

                return taskToAwait == null || taskToAwait.IsCompleted ? Observable.Return(value) : taskToAwait.ToObservable().Select(_ => value);
            })
            .Switch()
            .Select(filePath =>
            {
                var retryCnt = 0;
                return Observable.FromAsync(() => taskToComplete(filePath, previousTaskCancellationSource.Token)).Select(fileContent => (filePath, fileContent))
                    .Delay(_ => Observable.Timer(TimeSpan.FromMilliseconds(retryCnt++ * 100)))
                    .Retry(10);
            })
            .Switch();
        
    }

    /// <summary>
    /// Emit value on the leading edge of an interval, but suppress new values until <see cref="dueTime"/> has completed.
    /// </summary>
    public static IObservable<TSource> LeadingSample<TSource>(this IObservable<TSource> source, TimeSpan dueTime)
    {
        // var a = new Random().Next(0, 10);
        var delay = Observable.Empty<TSource>().Delay(dueTime);
        return source.Take(1).Concat(delay).Repeat(); 
    }

    public static Task FileIOTimeoutUnit(int increment) => Task.Delay(increment * 100);

    public static Task RetryAsync(Task task, int retryCnt)
    {
        var idx = 0;
        return task.ToObservable()
            .Delay(_ => Observable.Timer(TimeSpan.FromMilliseconds(idx++ * 100)))
            .Retry(retryCnt)
            .FirstAsync()
            .ToTask();
    }

    public static IObservable<TSource> Trace<TSource>(this IObservable<TSource> source, string name)
    {
        int id = 0;
        return Observable.Create<TSource>(observer => 
        {
            var id1 = ++id;
            Action<string, object> trace = (m, v) => Debug.WriteLine("{0} {1}: {2}({3})", name, id1, m, v);
            trace("Subscribe", "");
            var disposable = source.Subscribe(
                v => { trace("OnNext", null); observer.OnNext(v); },
                e => { trace("OnError", ""); observer.OnError(e); },
                () => { trace("OnCompleted", ""); observer.OnCompleted(); });
            return () => { trace("Dispose", ""); disposable.Dispose(); };
        });
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="usedTypes">Types used by the <see cref="usingType"/>.</param>
    /// <param name="usingType">Null if types provided by the generators.</param>
    /// <param name="usedTypesCollection">Collection of used types.</param>
    public static HashSet<UsedTypeSymbolLocation> UpdateUsedTypesCollection(List<TypeSymbolLocation> usedTypes, TypeSymbolLocation? usingType, HashSet<UsedTypeSymbolLocation> usedTypesCollection)
    {
        if (usedTypes == null && usingType == null) return usedTypesCollection;
        
        // Remove entries where usingType no longer uses them
        var entriesNoLongerUsedByType = usedTypesCollection.Where(e => e.UsingTypeLocation == usingType && (!usedTypes?.Contains(e.Location) ?? true)).ToList();

        usedTypesCollection.ExceptWith(entriesNoLongerUsedByType);

        if (usedTypes != null)
            foreach (var usedType in usedTypes)
                usedTypesCollection.Add(new UsedTypeSymbolLocation(usedType, usingType));
        
        // Find entries no longer used
        var entriesNoLongerUsedByAnyType = usedTypesCollection.Select(e => e.UsingTypeLocation).Where(e => e != null && usedTypesCollection.All(ee => ee.Location != e)).ToList();
        // Remove them
        foreach (var entry in entriesNoLongerUsedByAnyType)
            usedTypesCollection = UpdateUsedTypesCollection(null, entry, usedTypesCollection);

        return usedTypesCollection;
    }

    public class LocationsComparer : IEqualityComparer<List<TypeSymbolLocation>>
    {
        public bool Equals(List<TypeSymbolLocation> x, List<TypeSymbolLocation> y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;
            if (x.GetType() != y.GetType()) return false;
            return x.Count == y.Count && !x.Except(y).Any();
        }

        public int GetHashCode(List<TypeSymbolLocation> obj)
        {
            return HashCode.Combine(obj.Capacity, obj.Count);
        }
    }
    

    public static string GetAutogeneratedText(string description = null) => $@"//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>{(description == null ? null : $@"
//
// <description>
//     {string.Join("\n//     ", description.Split("\n"))}
// </description>")}
//------------------------------------------------------------------------------
";

}