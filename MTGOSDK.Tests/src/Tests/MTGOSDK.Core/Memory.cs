/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.Core.Remoting;


namespace MTGOSDK.Tests.MTGOSDK_Core;

[NonParallelizable]
public class Memory : BaseFixture
{
  [Test]
  public void Test_HeapSnapshot_ReturnsLiveObjectStatistics()
  {
    var snapshot = RemoteClient.GetHeapSnapshot(topN: 1);
    Assert.That(snapshot, Is.Not.Null);
    Assert.Multiple(() =>
    {
      Assert.That(snapshot.TotalObjectCount, Is.GreaterThan(0));
      Assert.That(snapshot.TotalHeapSize, Is.GreaterThan(0));
      Assert.That(snapshot.Types, Has.Count.EqualTo(1));
    });

    var largestType = snapshot.Types[0];
    Assert.Multiple(() =>
    {
      Assert.That(largestType.TypeName, Is.Not.Null.And.Not.Empty);
      Assert.That(largestType.Count, Is.GreaterThan(0));
      Assert.That(largestType.TotalSize, Is.GreaterThan(0));
    });
  }
}
