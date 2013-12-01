using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TDDKatas
{
  [TestFixture]
  public class RefreshingCacheTest
  {
    private static readonly TimeSpan DefaultCacheTimeToLive = TimeSpan.FromMinutes(1);

    [Test]
    public void EmptyCache_GetAnObject_SlowServiceIsCalled()
    {
      var slowService = new Mock<ISlowService>();
      var clock = BuildClockMock();
      var cache = new RefreshingCache(slowService.Object, clock.Object, new CacheConfig(DefaultCacheTimeToLive));

      cache.GetItem("key");

      slowService.Verify(ss => ss.Get("key"), Times.Once());
    }

    private static Mock<IClock> BuildClockMock()
    {
      var clock = new Mock<IClock>();
      clock.Setup(c => c.GetTime()).Returns(DateTime.Now);
      return clock;
    }

    [Test]
    public void CacheWithOneObject_GetObjectAgain_SlowServiceIsNotCalled()
    {
      var slowService = new Mock<ISlowService>();
      var clock = BuildClockMock();
      var cache = new RefreshingCache(slowService.Object, clock.Object, new CacheConfig(DefaultCacheTimeToLive));

      cache.GetItem("key");
      cache.GetItem("key");

      slowService.Verify(ss => ss.Get("key"), Times.Once());
    }

    [Test]
    public void CacheWithOneObject_IfObjectIsStale_SlowServiceIsCalled()
    {
      var slowService = new Mock<ISlowService>();
      var clock = BuildClockMock();
      var cache = new RefreshingCache(slowService.Object, clock.Object, new CacheConfig(DefaultCacheTimeToLive));

      cache.GetItem("key"); // this will put the object in cache

      // time passes
      clock.Setup(c => c.GetTime()).Returns(DateTime.Now.Add(TimeSpan.FromMinutes(5)));

      cache.GetItem("key");

      slowService.Verify(ss => ss.Get("key"), Times.Exactly(2));
    }

    [Test]
    public void CacheWithOneObject_IfAnotherObjectIsRequested_SlowServiceIsCalled()
    {
      var slowService = new Mock<ISlowService>();
      var clock = BuildClockMock();
      var cache = new RefreshingCache(slowService.Object, clock.Object, new CacheConfig(DefaultCacheTimeToLive));

      cache.GetItem("key1");
      cache.GetItem("key2");

      slowService.Verify(ss => ss.Get("key1"), Times.Once);
      slowService.Verify(ss => ss.Get("key2"), Times.Once);
    }

    [Test]
    public void CacheWithMaxSizeObjects_IfAnotherObjectIsRequested_OldestOneneIsRemoved()
    {
      var slowService = new Mock<ISlowService>();
      var clock = BuildClockMock();
      var cache = new RefreshingCache(slowService.Object, clock.Object, new CacheConfig(DefaultCacheTimeToLive, size: 3));

      cache.GetItem("key1");
      cache.GetItem("key2");
      cache.GetItem("key3");

      slowService.Verify(ss => ss.Get("key1"), Times.Once);


      // cache is now full. Request another item
      cache.GetItem("key4");

      // object with key1 should have been evicted now
      slowService.ResetCalls();
      cache.GetItem("key1");

      // verify that we have called it again
      slowService.Verify(ss => ss.Get("key1"), Times.Once);
    }
  }

  public class RefreshingCache
  {
    private readonly ISlowService _slowService;
    private readonly IClock _clock;
    private readonly TimeSpan _timeToLive;

    private readonly IDictionary<string, CachedItem> _items;
    private readonly uint _maxSize;

    public RefreshingCache(ISlowService slowService, IClock clock, CacheConfig cacheConfig)
    {
      _slowService = slowService;
      _clock = clock;
      _timeToLive = cacheConfig.TimeToLive;
      _maxSize = cacheConfig.Size;
      _items = new Dictionary<string, CachedItem>((int)_maxSize);
    }

    public object GetItem(string key)
    {
      if (!_items.ContainsKey(key))
      {
        RefreshItem(key);
      }
      else
      {
        if (ItemIsStale(key))
        {
          RefreshItem(key);
        }
      }

      return _items[key].Item;
    }

    private bool ItemIsStale(string key)
    {
      var currentTime = _clock.GetTime();
      return currentTime - _items[key].Timestamp > _timeToLive;
    }

    private void RefreshItem(string key)
    {
      EvictOldestItemIfNeeded();

      _items[key] = new CachedItem
      {
        Timestamp = _clock.GetTime(),
        Item = _slowService.Get(key)
      };
    }

    private void EvictOldestItemIfNeeded()
    {
      if (_items.Count == _maxSize)
      {
        var oldestItemKey = _items.OrderBy(i => i.Value.Timestamp).First().Key;
        _items.Remove(oldestItemKey);
      }
    }

    private class CachedItem
    {
      public DateTime Timestamp { get; set; }

      public object Item { get; set; }
    }
  }

  public class CacheConfig
  {
    public CacheConfig(TimeSpan timeToLive, uint size = 100)
    {
      TimeToLive = timeToLive;
      Size = size;
    }

    public TimeSpan TimeToLive { get; private set; }

    public uint Size { get; private set; }
  }

  public interface ISlowService
  {
    object Get(string key);
  }

  public interface IClock
  {
    DateTime GetTime();
  }
}