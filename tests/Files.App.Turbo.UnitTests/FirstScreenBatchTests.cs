// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class FirstScreenBatchTests
	{
		[TestMethod]
		[DataRow(1100d, 900d, 80d, 120d, 117)]
		[DataRow(1d, 2160d, 1d, 28d, 79)]
		[DataRow(200d, 100d, 80d, 120d, 64)]
		[DataRow(10000d, 10000d, 20d, 20d, 512)]
		public void Batch_covers_the_viewport_with_one_extra_row_and_stays_bounded(double width, double height, double itemWidth, double itemHeight, int expected)
			=> Assert.AreEqual(expected, FirstScreenBatch.GetCount(width, height, itemWidth, itemHeight));

		[TestMethod]
		public void Unmeasured_or_invalid_layout_uses_the_existing_default()
		{
			Assert.AreEqual(64, FirstScreenBatch.GetCount(0, 1000, 80, 120));
			Assert.AreEqual(64, FirstScreenBatch.GetCount(1000, 1000, 0, 120));
			Assert.AreEqual(64, FirstScreenBatch.GetCount(double.NaN, 1000, 80, 120));
			Assert.AreEqual(64, FirstScreenBatch.GetCount(1000, double.PositiveInfinity, 80, 120));
		}
	}
}
