using System;

namespace UniRx
{
    public static partial class Observable
    {
        /// <summary>
        /// Constructs an observable sequence that depends on a resource object, whose lifetime is
        /// tied to the resulting observable sequence's lifetime.
        /// </summary>
        public static IObservable<TSource> Using<TSource, TResource>(Func<TResource> resourceFactory, Func<TResource, IObservable<TSource>> observableFactory)
            where TResource : IDisposable
        {
            if (resourceFactory == null) throw new ArgumentNullException("resourceFactory");
            if (observableFactory == null) throw new ArgumentNullException("observableFactory");

            return Observable.Create<TSource>(observer =>
            {
                var resource = default(TResource);
                var resourceDisposable = Disposable.Empty;
                IObservable<TSource> source;

                try
                {
                    resource = resourceFactory();
                    if (resource != null) resourceDisposable = resource;
                    source = observableFactory(resource);
                }
                catch (Exception exception)
                {
                    source = Observable.Throw<TSource>(exception);
                }

                try
                {
                    return StableCompositeDisposable.Create(source.Subscribe(observer), resourceDisposable);
                }
                catch
                {
                    // Subscribe(observer) can throw synchronously (e.g. a synchronous source whose
                    // consumer throws from OnNext/OnError), in which case this method never reaches
                    // its return statement and nothing else would ever dispose resourceDisposable.
                    resourceDisposable.Dispose();
                    throw;
                }
            });
        }
    }
}
