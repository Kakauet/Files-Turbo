using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var output = Path.GetFullPath(args.Length == 0 ? Path.Combine(workspace, "reports/phase4-thumbnail-contracts.json") : args[0]);
if (!output.StartsWith(workspace, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Output outside workspace.");
UiPerformanceTrace.FixtureRoot = Path.Combine(workspace, "fixtures/phase4-thumbnail-contracts");
var results = new List<object>();

await Run("cached_first_paint_is_one_extraction_and_one_decode", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	Assert(initial.ThumbnailApplied && initial.CacheQueried, "Initial cache hit not reported.");
	await shell.TestLoad(item, lifetime.Token, initial);
	Assert(Win32Helper.Requests.Count == 1 && BitmapHelper.Calls == 1 && Win32Helper.Overlays == 1, "Cached first image was extracted/decoded again.");
	Assert(Win32Helper.Requests.All(request => request.Apartment == ApartmentState.STA), "Native work left STA.");
});

await Run("direct_cached_load_does_not_generate_again", async () =>
{
	using var shell = Fresh(); var item = Item();
	await shell.TestLoad(item, CancellationToken.None);
	Assert(Win32Helper.Requests.Count == 1 && BitmapHelper.Calls == 1, "Cache hit caused a full provider extraction.");
});

await Run("failed_cached_decode_retries_with_full_thumbnail", async () =>
{
	using var shell = Fresh(); var item = Item();
	BitmapHelper.Handler = data => Task.FromResult<BitmapImage?>(BitmapHelper.Calls == 1 ? null : new(data[0]));
	await shell.TestLoad(item, CancellationToken.None);
	Assert(Win32Helper.Requests.Count == 2 && BitmapHelper.Calls == 2 && item.FileImage is not null, "Failed cached decode never recovered.");
});

await Run("cache_miss_is_shared_with_full_load_and_generates_once", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	Win32Helper.IconHandler = request => request.Options.HasFlag(IconOptions.ReturnOnlyIfCached) ? null : [42];
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	Assert(initial.CacheQueried && !initial.ThumbnailApplied && item.FileImage?.Marker == 200, "First-paint fallback lost.");
	await shell.TestLoad(item, lifetime.Token, initial);
	Assert(Win32Helper.Requests.Count == 2 && BitmapHelper.Calls == 1 && item.FileImage?.Marker == 42, "Cache miss was queried twice or generation lost.");
});

await Run("default_initial_result_and_direct_load_still_apply_generic_icon", async () =>
{
	using var shell = Fresh(); var item = Item(); item.PreloadedIconData = [200]; item.FileImage = new(199);
	ShellViewModel.InitialThumbnailLoadResult initial = default;
	Assert(initial.PreloadedIconImage is null && !initial.LoadCompleted, "Default result acquired an image or completion.");
	Win32Helper.IconHandler = request =>
	{
		if (request.Options.HasFlag(IconOptions.ReturnOnlyIfCached)) return null;
		Assert(shell.TestIconImageCalls == 1 && item.FileImage?.Marker == 200, "Direct load skipped its generic icon before generation.");
		return [42];
	};
	await shell.TestLoad(item, CancellationToken.None, initial);
	Assert(shell.TestIconImageCalls == 1 && shell.TestDispatcherPriorities.Count(priority => priority == Microsoft.UI.Dispatching.DispatcherQueuePriority.Low) == 1 && item.FileImage?.Marker == 42,
		"Direct generic fallback or subsequent thumbnail changed.");
});

await Run("initial_generic_reference_skips_second_low_apply_before_native_generation", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	Win32Helper.IconHandler = request =>
	{
		if (request.Options.HasFlag(IconOptions.ReturnOnlyIfCached)) return null;
		Assert(shell.TestIconImageCalls == 1 && shell.TestDispatcherPriorities.All(priority => priority != Microsoft.UI.Dispatching.DispatcherQueuePriority.Low),
			"Native generation waited for a duplicate generic icon Low apply.");
		return [42];
	};
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	Assert(initial.PreloadedIconImage is not null && ReferenceEquals(initial.PreloadedIconImage, item.FileImage), "Initial result did not retain the assigned shared image reference.");
	await shell.TestLoad(item, lifetime.Token, initial);
	Assert(shell.TestIconImageCalls == 1 && Win32Helper.Requests.Count == 2 && item.FileImage?.Marker == 42, "Generic icon was decoded again or generation did not finish.");
});

await Run("null_initial_generic_image_retries_before_native_generation", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	shell.TestIconImageHandler = _ => Task.FromResult<BitmapImage?>(shell.TestIconImageCalls == 1 ? null : new(200));
	Win32Helper.IconHandler = request =>
	{
		if (request.Options.HasFlag(IconOptions.ReturnOnlyIfCached)) return null;
		Assert(shell.TestIconImageCalls == 2 && item.FileImage?.Marker == 200, "A failed initial shared icon suppressed its retry.");
		return [42];
	};
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	Assert(initial.PreloadedIconImage is null && item.FileImage is null, "A failed initial icon was recorded as assigned.");
	await shell.TestLoad(item, lifetime.Token, initial);
	Assert(shell.TestIconImageCalls == 2 && shell.TestDispatcherPriorities.Count(priority => priority == Microsoft.UI.Dispatching.DispatcherQueuePriority.Low) == 1 && item.FileImage?.Marker == 42,
		"Failed initial generic icon did not recover conservatively.");
});

await Run("failed_initial_cached_decode_captures_generic_and_generates_without_duplicate", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	BitmapHelper.Handler = data => Task.FromResult<BitmapImage?>(BitmapHelper.Calls == 1 ? null : new(data[0]));
	Win32Helper.IconHandler = request => request.Options.HasFlag(IconOptions.ReturnOnlyIfCached) ? [48] : [42];
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	Assert(initial.CacheQueried && !initial.ThumbnailApplied && ReferenceEquals(initial.PreloadedIconImage, item.FileImage) && item.FileImage?.Marker == 200,
		"Failed cached decode did not retain its assigned fallback image.");
	await shell.TestLoad(item, lifetime.Token, initial);
	Assert(shell.TestIconImageCalls == 1 && Win32Helper.Requests.Count == 2 && BitmapHelper.Calls == 2 && item.FileImage?.Marker == 42,
		"Cached decode failure duplicated generic application or lost full generation.");
});

await Run("replaced_initial_generic_image_is_reapplied_conservatively", () => CheckInvalidatedGeneric((_, item) => item.FileImage = new(199), requireInvalidState: false));
await Run("cleared_initial_generic_image_is_reapplied_conservatively", () => CheckInvalidatedGeneric((_, item) => item.FileImage = null, requireInvalidState: false));
await Run("generic_image_size_change_reapplies_shared_icon", () => CheckInvalidatedGeneric((shell, _) => shell.Size = 128));
await Run("generic_image_dpi_change_reapplies_shared_icon", () => CheckInvalidatedGeneric((_, _) => App.AppModel.AppWindowDPI = 2));
await Run("generic_image_path_change_reapplies_shared_icon", () => CheckInvalidatedGeneric((_, item) => item.ItemPath = Path.Combine(UiPerformanceTrace.FixtureRoot, "renamed-generic.bmp")));
await Run("generic_image_modified_time_change_reapplies_shared_icon", () => CheckInvalidatedGeneric((_, item) => item.ItemDateModifiedReal = item.ItemDateModifiedReal.AddSeconds(1)));
await Run("generic_image_preference_change_reapplies_shared_icon", () => CheckInvalidatedGeneric((shell, _) => shell.ShowThumbnails = false));
await Run("generic_image_navigation_generation_change_reapplies_shared_icon", () => CheckInvalidatedGeneric((shell, _) => shell.NextGeneration()));

await Run("generic_image_different_item_reapplies_shared_icon", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	Win32Helper.IconHandler = request => request.Options.HasFlag(IconOptions.ReturnOnlyIfCached) ? null : [42];
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	var other = Item(); other.PreloadedIconData = item.PreloadedIconData; other.FileImage = initial.PreloadedIconImage;
	Assert(!shell.TestCanReuse(other, lifetime.Token, initial), "Another item accepted the generic image handoff.");
	await shell.TestLoad(other, lifetime.Token, initial);
	Assert(shell.TestIconImageCalls == 2 && shell.TestDispatcherPriorities.Count(priority => priority == Microsoft.UI.Dispatching.DispatcherQueuePriority.Low) == 1 && other.FileImage?.Marker == 42,
		"Another item skipped generic application or full thumbnail generation.");
});

await Run("generic_image_canceled_row_token_reapplies_for_new_lifetime", async () =>
{
	using var shell = Fresh(); using var firstLifetime = new CancellationTokenSource(); using var nextLifetime = new CancellationTokenSource(); var item = Item();
	Win32Helper.IconHandler = request => request.Options.HasFlag(IconOptions.ReturnOnlyIfCached) ? null : [42];
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, firstLifetime.Token);
	firstLifetime.Cancel();
	Assert(initial.Token.IsCancellationRequested && !shell.TestCanReuse(item, nextLifetime.Token, initial), "Canceled row token was accepted by the new lifetime.");
	await shell.TestLoad(item, nextLifetime.Token, initial);
	Assert(shell.TestIconImageCalls == 2 && shell.TestDispatcherPriorities.Count(priority => priority == Microsoft.UI.Dispatching.DispatcherQueuePriority.Low) == 1 && item.FileImage?.Marker == 42,
		"New row lifetime skipped conservative icon application.");
});

await Run("size_change_invalidates_first_paint_result", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	shell.Size = 128;
	Assert(!shell.TestCanReuse(item, lifetime.Token, initial), "Old logical size was accepted.");
	await shell.TestLoad(item, lifetime.Token, initial);
	Assert(Win32Helper.Requests.Count == 2 && item.FileImage?.Marker == 128, "Old image size lingered.");
});

await Run("dpi_change_invalidates_first_paint_result", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	App.AppModel.AppWindowDPI = 2;
	Assert(!shell.TestCanReuse(item, lifetime.Token, initial), "Old DPI was accepted.");
	await shell.TestLoad(item, lifetime.Token, initial);
	Assert(Win32Helper.Requests.Count == 2 && item.FileImage?.Marker == 96, "Old physical size lingered.");
});

await Run("high_dpi_thumbnail_requests_full_physical_resolution", async () =>
{
	using var shell = Fresh(); App.AppModel.AppWindowDPI = 2;
	await FileThumbnailHelper.GetIconAsync(Item().ItemPath, 256, false, IconOptions.ReturnThumbnailOnly);
	Assert(Win32Helper.Requests.Single().Size == 512, "256 DIP at DPI2 was capped below 512 physical pixels.");
});

await Run("thumbnail_decode_matches_high_dpi_display_size", async () =>
{
	using var shell = Fresh(); shell.Size = 256; App.AppModel.AppWindowDPI = 2;
	await shell.TestLoad(Item(), CancellationToken.None);
	Assert(BitmapHelper.DecodeRequests.Single() == (512, DecodePixelType.Physical, true, true, false), "Thumbnail decode does not match 512 physical pixels, preserve aspect, or keep a large decode asynchronous.");
});

await Run("thumbnail_decode_rounds_fractional_dpi_up", async () =>
{
	using var shell = Fresh(); shell.Size = 171; App.AppModel.AppWindowDPI = 1.5f;
	await shell.TestLoad(Item(), CancellationToken.None);
	Assert(BitmapHelper.DecodeRequests.Single() == (257, DecodePixelType.Physical, true, true, false), "Thumbnail decode undersamples a fractional physical size or runs a large decode synchronously.");
});

await Run("png_landscape_decode_constrains_width_only", () =>
{
	var image = BitmapHelper.TestDecodeSize(PngHeader(400, 200), 96, true);
	Assert(image.DecodePixelWidth == 96 && image.DecodePixelHeight == 0, "Landscape decode assigned a square or wrong side.");
	return Task.CompletedTask;
});

await Run("png_portrait_decode_constrains_height_only_at_physical_target", () =>
{
	var image = BitmapHelper.TestDecodeSize(PngHeader(200, 400), 512, true);
	Assert(image.DecodePixelWidth == 0 && image.DecodePixelHeight == 512, "Portrait decode ignored height or altered the physical target.");
	return Task.CompletedTask;
});

await Run("png_square_decode_still_uses_one_dimension", () =>
{
	var image = BitmapHelper.TestDecodeSize(PngHeader(400, 400), 96, true);
	Assert(image.DecodePixelWidth == 96 && image.DecodePixelHeight == 0, "Square decode unnecessarily constrained both dimensions.");
	return Task.CompletedTask;
});

await Run("png_dimensions_are_read_big_endian", () =>
{
	var landscape = BitmapHelper.TestDecodeSize(PngHeader(65536, 256), 96, true);
	var portrait = BitmapHelper.TestDecodeSize(PngHeader(256, 65536), 96, true);
	Assert(landscape.DecodePixelWidth == 96 && landscape.DecodePixelHeight == 0 &&
		portrait.DecodePixelWidth == 0 && portrait.DecodePixelHeight == 96, "PNG dimensions were read in host/little endian order.");
	return Task.CompletedTask;
});

await Run("malformed_png_headers_fall_back_without_bounds_errors", () =>
{
	var badSignature = PngHeader(200, 400); badSignature[0] = 0;
	var badType = PngHeader(200, 400); badType[12] = 0;
	var badLength = PngHeader(200, 400); BinaryPrimitives.WriteUInt32BigEndian(badLength.AsSpan(8, 4), 12);
	var malformed = new[] { Array.Empty<byte>(), new byte[7], PngHeader(200, 400)[..24], PngHeader(200, 400)[..32],
		badSignature, badType, badLength, PngHeader(0, 400), PngHeader(200, 0), PngHeader(0x80000000, 400), PngHeader(200, 0x80000000) };
	foreach (var data in malformed)
	{
		var image = BitmapHelper.TestDecodeSize(data, 96, true);
		Assert(image.DecodePixelWidth == 96 && image.DecodePixelHeight == 0, "Malformed header altered aspect-safe fallback.");
	}
	return Task.CompletedTask;
});

await Run("non_png_rasters_keep_one_dimension_fallback", () =>
{
	var image = BitmapHelper.TestDecodeSize([255, 216, 255, 224], 96, true);
	Assert(image.DecodePixelWidth == 96 && image.DecodePixelHeight == 0, "Unknown raster forced a square decode.");
	return Task.CompletedTask;
});

await Run("legacy_decode_callers_keep_both_dimensions", () =>
{
	var image = BitmapHelper.TestDecodeSize(PngHeader(200, 400), 96, false);
	Assert(image.DecodePixelWidth == 96 && image.DecodePixelHeight == 96, "Existing caller decode behavior changed without opt-in.");
	return Task.CompletedTask;
});

await Run("unspecified_decode_size_remains_unconstrained", () =>
{
	foreach (var size in new[] { -1, 0 })
	foreach (var preserve in new[] { false, true })
	{
		var image = BitmapHelper.TestDecodeSize(PngHeader(200, 400), size, preserve);
		Assert(image.DecodePixelWidth == 0 && image.DecodePixelHeight == 0, "Unspecified decode target set an invalid dimension.");
	}
	return Task.CompletedTask;
});

await Run("synchronous_small_thumbnail_requires_explicit_physical_aspect_opt_in", () =>
{
	var data = PngHeader(128, 64);
	Assert(BitmapHelper.TestSynchronousSelection(data), "Eligible small PNG was not selected.");
	Assert(!BitmapHelper.TestSynchronousSelection(data, useSynchronousSmallThumbnail: false) &&
		!BitmapHelper.TestSynchronousSelection(data, preserveAspectRatio: false) &&
		!BitmapHelper.TestSynchronousSelection(data, decodePixelType: DecodePixelType.Logical), "Non-thumbnail/default caller enabled synchronous decoding.");
	return Task.CompletedTask;
});

await Run("synchronous_small_thumbnail_target_is_bounded_to_256_physical_pixels", () =>
{
	var data = PngHeader(256, 128);
	foreach (var target in new[] { 1, 256 })
		Assert(BitmapHelper.TestSynchronousSelection(data, target), "Inclusive physical decode boundary was rejected.");
	foreach (var target in new[] { -1, 0, 257, 512, int.MaxValue })
		Assert(!BitmapHelper.TestSynchronousSelection(data, target), "Unspecified/high-DPI decode entered the synchronous path.");
	return Task.CompletedTask;
});

await Run("synchronous_small_thumbnail_encoded_bytes_are_bounded_to_256_kib", () =>
{
	foreach (var length in new[] { 33, 256 * 1024, 256 * 1024 + 1 })
	{
		var data = new byte[length]; PngHeader(128, 64).CopyTo(data, 0);
		Assert(BitmapHelper.TestSynchronousSelection(data) == (length <= 256 * 1024), "Encoded byte boundary was not enforced.");
	}
	return Task.CompletedTask;
});

await Run("synchronous_small_thumbnail_source_dimensions_are_bounded_on_both_axes", () =>
{
	foreach (var pair in new (uint Width, uint Height)[] { (1, 1), (256, 1), (1, 256), (256, 256), (128, 256), (256, 128) })
		Assert(BitmapHelper.TestSynchronousSelection(PngHeader(pair.Width, pair.Height)), "Valid source boundary or big-endian dimensions were rejected.");
	foreach (var pair in new (uint Width, uint Height)[] { (0, 1), (1, 0), (257, 1), (1, 257), (65536, 256), (256, 65536), (0x80000000, 1), (1, 0x80000000) })
		Assert(!BitmapHelper.TestSynchronousSelection(PngHeader(pair.Width, pair.Height)), "Large/invalid source dimensions entered the synchronous path.");
	return Task.CompletedTask;
});

await Run("synchronous_small_thumbnail_rejects_unknown_and_malformed_headers", () =>
{
	foreach (var truncated in new[] { Array.Empty<byte>(), new byte[7], PngHeader(128, 64)[..24], PngHeader(128, 64)[..32], new byte[] { 255, 216, 255, 224 } })
		Assert(!BitmapHelper.TestSynchronousSelection(truncated), "Truncated/unknown raster entered the synchronous path.");
	for (var index = 0; index < 8; index++)
	{
		var data = PngHeader(128, 64); data[index] ^= 1;
		Assert(!BitmapHelper.TestSynchronousSelection(data), "Incomplete PNG signature was accepted.");
	}
	var badType = PngHeader(128, 64); badType[12] = 0;
	var badLength = PngHeader(128, 64); BinaryPrimitives.WriteUInt32BigEndian(badLength.AsSpan(8, 4), 12);
	Assert(!BitmapHelper.TestSynchronousSelection(badType) && !BitmapHelper.TestSynchronousSelection(badLength), "Invalid first IHDR chunk entered the synchronous path.");
	return Task.CompletedTask;
});

await Run("small_decode_target_does_not_make_large_png_source_synchronous", () =>
{
	Assert(!BitmapHelper.TestSynchronousSelection(PngHeader(512, 512), 96), "Small display target bypassed the encoded source dimension guard.");
	return Task.CompletedTask;
});

await Run("thumbnail_caller_requests_and_selects_sync_only_for_eligible_small_png", async () =>
{
	using var shell = Fresh(); shell.Size = 96;
	Win32Helper.IconHandler = _ => PngHeader(128, 64);
	await shell.TestLoad(Item(), CancellationToken.None);
	Assert(BitmapHelper.DecodeRequests.Single() == (96, DecodePixelType.Physical, true, true, true), "Thumbnail caller did not select the bounded synchronous mode.");
});

await Run("legacy_bitmap_caller_keeps_async_default_without_opt_in", async () =>
{
	using var shell = Fresh();
	await PngHeader(128, 64).ToBitmapAsync();
	Assert(BitmapHelper.DecodeRequests.Single() == (-1, DecodePixelType.Logical, false, false, false), "Existing bitmap caller changed its decode mode.");
});

await Run("above_largest_bucket_rounds_up_without_undersampling", async () =>
{
	using var shell = Fresh(); App.AppModel.AppWindowDPI = 1.5f;
	await FileThumbnailHelper.GetIconAsync(Item().ItemPath, 171, false, IconOptions.ReturnThumbnailOnly);
	await FileThumbnailHelper.GetIconAsync(Item().ItemPath, 171, false, IconOptions.ReturnIconOnly | IconOptions.SkipSizeSnapping);
	Assert(Win32Helper.Requests.All(request => request.Size == 257), "256.5 physical pixels were rounded below the display requirement.");
});

await Run("standard_buckets_and_small_icon_rounding_are_preserved", async () =>
{
	using var shell = Fresh();
	foreach (var pair in new (uint Requested, int Expected)[] { (1, 16), (16, 16), (17, 24), (25, 32), (33, 48), (49, 64), (65, 96), (97, 128), (129, 256), (256, 256) })
	{
		await FileThumbnailHelper.GetIconAsync(Item().ItemPath, pair.Requested, false, IconOptions.ReturnThumbnailOnly);
		Assert(Win32Helper.Requests.Last().Size == pair.Expected, "A standard bucket changed.");
	}
	App.AppModel.AppWindowDPI = 1.5f;
	await FileThumbnailHelper.GetIconAsync(Item().ItemPath, 15, false, IconOptions.ReturnIconOnly | IconOptions.SkipSizeSnapping);
	Assert(Win32Helper.Requests.Last().Size == 22, "Existing midpoint-to-even rounding for sidebar-size icons changed.");
	App.AppModel.AppWindowDPI = 2;
	await FileThumbnailHelper.GetIconAsync(Item().ItemPath, 128, false, IconOptions.ReturnThumbnailOnly);
	Assert(Win32Helper.Requests.Last().Size == 256, "The exact largest bucket changed.");
});

await Run("modified_time_invalidates_first_paint_result", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	item.ItemDateModifiedReal = item.ItemDateModifiedReal.AddSeconds(1);
	Win32Helper.IconHandler = _ => [42];
	Assert(!shell.TestCanReuse(item, lifetime.Token, initial), "Old file revision was accepted.");
	await shell.TestLoad(item, lifetime.Token, initial);
	Assert(Win32Helper.Requests.Count == 2 && item.FileImage?.Marker == 42, "Modified item reused an old image.");
});

await Run("renamed_path_invalidates_first_paint_result", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	item.ItemPath = Path.Combine(UiPerformanceTrace.FixtureRoot, "renamed.bmp");
	Assert(!shell.TestCanReuse(item, lifetime.Token, initial), "Old path was accepted.");
	await shell.TestLoad(item, lifetime.Token, initial);
	Assert(Win32Helper.Requests.Last().Path == item.ItemPath && Win32Helper.Requests.Count == 2, "New path was never queried.");
});

await Run("navigation_generation_invalidates_first_paint_result", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	shell.NextGeneration();
	Assert(initial.Token.IsCancellationRequested && !shell.TestCanReuse(item, lifetime.Token, initial), "Previous navigation lifetime was reused.");
	await shell.TestLoad(item, lifetime.Token, initial);
	Assert(Win32Helper.Requests.Count == 2, "New generation skipped its cache query.");
});

await Run("different_item_cannot_reuse_first_paint_result", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	var other = Item();
	Assert(!shell.TestCanReuse(other, lifetime.Token, initial), "Different object reused the image state.");
	await shell.TestLoad(other, lifetime.Token, initial);
	Assert(Win32Helper.Requests.Count == 2 && other.FileImage is not null, "Second object lost its image.");
});

await Run("thumbnail_preference_change_restores_final_icon", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	shell.ShowThumbnails = false;
	Win32Helper.IconHandler = _ => [43];
	Assert(!shell.TestCanReuse(item, lifetime.Token, initial), "Old thumbnail preference was accepted.");
	await shell.TestLoad(item, lifetime.Token, initial);
	Assert(Win32Helper.Requests.Count == 2 && Win32Helper.Requests.Last().Options == IconOptions.ReturnIconOnly && item.FileImage?.Marker == 43, "Thumbnail remained after disabling thumbnails.");
});

await Run("image_is_applied_before_slow_overlay_finishes", async () =>
{
	using var shell = Fresh(); var item = Item(); using var started = new ManualResetEventSlim(); using var gate = new ManualResetEventSlim();
	Win32Helper.OverlayHandler = (_, _, folder) => { Assert(!folder, "File sent as folder to overlay API."); started.Set(); WaitGate(gate); return null; };
	var load = shell.TestLoad(item, CancellationToken.None);
	try
	{
		Assert(started.Wait(TimeSpan.FromSeconds(5)), "Overlay did not begin.");
		Assert(item.FileImage?.Marker == 48 && !load.IsCompleted, "Slow overlay delayed first image.");
	}
	finally { gate.Set(); }
	await load;
});

await Run("recycling_while_decode_runs_rejects_assignment", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
	var gate = new TaskCompletionSource<BitmapImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
	BitmapHelper.Handler = _ => { started.TrySetResult(); return gate.Task; };
	var load = shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); lifetime.Cancel(); gate.SetResult(new(42));
	await load;
	Assert(item.FileImage is null, "Canceled row received a late image.");
});

await Run("shortcut_and_embedded_icon_paths_remain_per_file", async () =>
{
	foreach (var extension in new[] { ".lnk", ".ico", ".exe" })
	{
		using var shell = Fresh(); shell.ShowThumbnails = false;
		var item = Item(); item.FileExtension = extension; item.IsShortcut = extension == ".lnk"; item.PreloadedIconData = [200];
		Win32Helper.IconHandler = _ => [44];
		await shell.TestLoad(item, CancellationToken.None);
		Assert(Win32Helper.Requests.Count == 1 && Win32Helper.Requests.Single().Path == item.ItemPath && item.FileImage?.Marker == 44, "Per-file icon replaced with generic icon.");
	}
});

await Run("viewport_waits_for_generated_thumbnail_and_marks_completed", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	using var started = new ManualResetEventSlim(); using var gate = new ManualResetEventSlim();
	Win32Helper.IconHandler = request =>
	{
		if (request.Options.HasFlag(IconOptions.ReturnOnlyIfCached)) return null;
		started.Set(); WaitGate(gate); return [45];
	};
	var load = shell.LoadViewportThumbnailAsync(item, lifetime.Token);
	try { Assert(started.Wait(TimeSpan.FromSeconds(5)) && !load.IsCompleted, "Viewport released its slot before generation finished."); }
	finally { gate.Set(); }
	var completed = await load;
	Assert(completed.LoadCompleted && shell.IsViewportThumbnailCurrent(item, completed) && shell.TestCanReuse(item, lifetime.Token, completed) && item.FileImage?.Marker == 45, "Completed viewport result lost.");
	item.ItemDateModifiedReal = item.ItemDateModifiedReal.AddSeconds(1);
	Assert(!shell.IsViewportThumbnailCurrent(item, completed) && !shell.TestCanReuse(item, lifetime.Token, completed), "Completed result survived item revision change.");
});

await Run("viewport_image_slot_completes_without_overlay_work", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	using var nativeStarted = new ManualResetEventSlim(); using var nativeGate = new ManualResetEventSlim();
	Win32Helper.IconHandler = request =>
	{
		if (request.Options.HasFlag(IconOptions.ReturnOnlyIfCached)) return null;
		nativeStarted.Set(); WaitGate(nativeGate); return [49];
	};
	Win32Helper.OverlayHandler = (_, _, _) => throw new InvalidOperationException("Viewport must not start overlay work.");
	var load = shell.LoadViewportThumbnailAsync(item, lifetime.Token);
	try
	{
		Assert(nativeStarted.Wait(TimeSpan.FromSeconds(5)) && Win32Helper.Overlays == 0 && !load.IsCompleted, "Viewport slot did not wait for the image provider alone.");
	}
	finally { nativeGate.Set(); }
	var completed = await load;
	Assert(shell.IsViewportThumbnailCurrent(item, completed) && item.FileImage?.Marker == 49 && Win32Helper.Overlays == 0, "Viewport image completion waited for or lost an overlay.");
	lifetime.Cancel();
	Assert(!shell.IsViewportThumbnailCurrent(item, completed), "Canceled image handoff was accepted.");
});

await Run("metadata_overlay_cancellation_waits_for_native_call_and_rejects_assignment", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	var completed = await shell.LoadViewportThumbnailAsync(item, lifetime.Token);
	var image = item.FileImage;
	Assert(completed.LoadCompleted && image is not null && Win32Helper.Overlays == 0, "Viewport already loaded overlay metadata.");
	using var started = new ManualResetEventSlim(); using var gate = new ManualResetEventSlim();
	Win32Helper.OverlayHandler = (_, _, _) => { started.Set(); WaitGate(gate); return [57]; };
	var metadata = shell.TestLoadOverlay(item, lifetime.Token);
	try
	{
		Assert(started.Wait(TimeSpan.FromSeconds(5)) && !metadata.IsCompleted, "Metadata did not wait for its native overlay.");
		lifetime.Cancel();
		Assert(!metadata.IsCompleted && ReferenceEquals(item.FileImage, image), "Cancellation released running overlay work or replaced the thumbnail.");
	}
	finally { gate.Set(); }
	await AssertCanceled(metadata);
	Assert(item.IconOverlay is null && item.ShieldIcon is null && ReferenceEquals(item.FileImage, image), "Canceled metadata published a late overlay/shield or altered its thumbnail.");
});

await Run("metadata_overlay_cancellation_during_decode_rejects_overlay_and_shield", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	await shell.LoadViewportThumbnailAsync(item, lifetime.Token);
	var image = item.FileImage;
	var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
	var gate = new TaskCompletionSource<BitmapImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
	Win32Helper.OverlayHandler = (_, _, _) => [58];
	BitmapHelper.Handler = _ => { started.TrySetResult(); return gate.Task; };
	var metadata = shell.TestLoadOverlay(item, lifetime.Token);
	try
	{
		await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		lifetime.Cancel();
	}
	finally { gate.TrySetResult(new(58)); }
	await AssertCanceled(metadata);
	Assert(item.IconOverlay is null && item.ShieldIcon is null && ReferenceEquals(item.FileImage, image), "Canceled overlay decode changed the current image or metadata.");
	Assert(BitmapHelper.DecodeRequests.Last() == (-1, DecodePixelType.Logical, false, false, false), "Overlay opted into thumbnail synchronous mode.");
});

await Run("failed_generation_keeps_event_retry_and_canceled_delay_cleans_up", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	Win32Helper.IconHandler = _ => null;
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	await shell.TestLoad(item, lifetime.Token, initial, retry: true);
	Assert(item.NeedsDelayedThumbnailLoad && shell.RetryCount == 1, "Failed thumbnail lost its fallback retry.");
	lifetime.Cancel(); await WaitUntil(() => shell.RetryCount == 0);
});

await Run("native_thumbnail_cancellation_waits_for_started_call", async () =>
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); using var started = new ManualResetEventSlim(); using var gate = new ManualResetEventSlim();
	Win32Helper.IconHandler = _ => { started.Set(); WaitGate(gate); return [46]; };
	var load = FileThumbnailHelper.GetIconAsync(Item().ItemPath, 48, false, IconOptions.ReturnThumbnailOnly, lifetime.Token);
	try
	{
		Assert(started.Wait(TimeSpan.FromSeconds(5)), "Native thumbnail did not begin.");
		lifetime.Cancel(); Assert(!load.IsCompleted, "Started call released its task before returning.");
	}
	finally { gate.Set(); }
	await AssertCanceled(load);
});

await Run("legacy_sta_cancellation_still_completes_promptly", async () =>
{
	using var lifetime = new CancellationTokenSource(); using var started = new ManualResetEventSlim(); using var gate = new ManualResetEventSlim();
	var load = STATask.RunPooled(() => { started.Set(); WaitGate(gate); return 47; }, null, lifetime.Token);
	try
	{
		Assert(started.Wait(TimeSpan.FromSeconds(5)), "Legacy native work did not begin.");
		lifetime.Cancel(); await AssertCanceled(load);
	}
	finally { gate.Set(); }
});

await Run("queued_thumbnail_cancellation_remains_prompt_and_never_executes", async () =>
{
	using var shell = Fresh(); using var started = new CountdownEvent(12); using var gate = new ManualResetEventSlim();
	var blockers = Enumerable.Range(0, 12).Select(_ => STATask.RunPooled(() => { started.Signal(); WaitGate(gate); return true; }, null)).ToArray();
	try
	{
		Assert(started.Wait(TimeSpan.FromSeconds(5)), "Pool did not saturate.");
		using var lifetime = new CancellationTokenSource();
		var load = FileThumbnailHelper.GetIconAsync(Item().ItemPath, 48, false, IconOptions.ReturnThumbnailOnly, lifetime.Token);
		lifetime.Cancel(); await AssertCanceled(load);
		Assert(Win32Helper.Requests.IsEmpty, "Canceled queued delegate executed.");
	}
	finally { gate.Set(); }
	await Task.WhenAll(blockers);
	await STATask.RunPooledUntilCompleted(() => true, null);
	Assert(Win32Helper.Requests.IsEmpty, "Canceled delegate ran when pool resumed.");
});

await Run("four_viewport_slots_survive_cancel_until_native_calls_return", async () =>
{
	using var shell = Fresh(); using var started = new CountdownEvent(4); using var gate = new ManualResetEventSlim();
	var calls = 0; var alive = 0; var maximum = 0;
	Win32Helper.IconHandler = _ =>
	{
		var active = Interlocked.Increment(ref alive);
		int previous; do { previous = Volatile.Read(ref maximum); if (active <= previous) break; } while (Interlocked.CompareExchange(ref maximum, active, previous) != previous);
		if (Interlocked.Increment(ref calls) <= 4) started.Signal();
		try { WaitGate(gate); return [48]; }
		finally { Interlocked.Decrement(ref alive); }
	};
	using var queue = new ViewportWorkQueue<object>(4, 128, action => { action(); return true; });
	Func<CancellationToken, Task> work = async token => { await FileThumbnailHelper.GetIconAsync(Item().ItemPath, 48, false, IconOptions.ReturnThumbnailOnly, token); };
	for (var index = 0; index < 8; index++) queue.Enqueue(new(), work);
	try
	{
		Assert(started.Wait(TimeSpan.FromSeconds(5)), "Four calls did not begin.");
		queue.CancelAll();
		for (var index = 0; index < 8; index++) queue.Enqueue(new(), work);
		Assert(queue.ActiveCount == 4 && Volatile.Read(ref calls) == 4, "Cancellation started replacements before native completion.");
	}
	finally { gate.Set(); }
	await WaitUntil(() => queue.ActiveCount == 0 && queue.PendingCount == 0);
	Assert(maximum == 4 && calls == 12, "Actual native concurrency exceeded four or replacement work was lost.");
});

await Run("stage_markers_are_opt_in_and_fixture_scoped", async () =>
{
	using var shell = Fresh(); var item = Item();
	await shell.LoadViewportThumbnailAsync(item, CancellationToken.None);
	Assert(UiPerformanceTrace.Records.IsEmpty, "Disabled instrumentation wrote records.");
	UiPerformanceTrace.IsEnabled = true;
	await FileThumbnailHelper.GetIconAsync("synthetic-outside.bmp", 48, false, IconOptions.ReturnThumbnailOnly, performanceOperationId: 7);
	Assert(UiPerformanceTrace.Records.IsEmpty, "Outside-fixture extraction wrote records.");
	item = Item();
	await shell.LoadViewportThumbnailAsync(item, CancellationToken.None);
	var milestones = UiPerformanceTrace.Records.Select(record => record.Milestone).ToHashSet();
	Assert(milestones.IsSupersetOf(["ThumbnailCacheQueueWait", "ThumbnailCacheStarted", "ThumbnailCacheCompleted", "ThumbnailDispatcherWait", "ThumbnailDecodeCompleted", "ThumbnailApplied"]) && !milestones.Contains("OverlayCompleted"), "Viewport stages incomplete or overlay ran in the image stage.");
	await shell.TestLoadOverlay(item, CancellationToken.None);
	Assert(UiPerformanceTrace.Records.Any(record => record.Milestone == "OverlayCompleted"), "Separate metadata overlay had no completion marker.");
	Assert(UiPerformanceTrace.Records.All(record => record.Duration >= 0), "Negative elapsed time.");
});

await Run("provider_exception_is_timed_and_pool_remains_available", async () =>
{
	using var shell = Fresh(); UiPerformanceTrace.IsEnabled = true;
	Win32Helper.IconHandler = _ => throw new IOException("Synthetic provider failure");
	var data = await FileThumbnailHelper.GetIconAsync(Item().ItemPath, 48, false, IconOptions.ReturnThumbnailOnly, performanceOperationId: 7);
	Assert(data is null && UiPerformanceTrace.Records.Any(record => record.Milestone == "ThumbnailNativeCompleted" && record.Value == 0), "Provider failure had no completion timing.");
	Win32Helper.IconHandler = _ => [50];
	data = await FileThumbnailHelper.GetIconAsync(Item().ItemPath, 48, false, IconOptions.ReturnThumbnailOnly);
	Assert(data is [50], "Provider exception stopped the pool.");
});

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var sources = new[] {
	"source/src/Files.App/ViewModels/ShellViewModel.cs",
	"source/src/Files.App/Helpers/BitmapHelper.cs",
	"source/src/Files.App/Utils/Storage/Helpers/FileThumbnailHelper.cs",
	"source/src/Files.App.Storage/Windows/Managers/STATask.cs",
	"source/src/Files.App/Helpers/ViewportWorkQueue.cs"
}.Select(path => new { Path = path, SHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(workspace, path)))) }).ToArray();
File.WriteAllText(output, JsonSerializer.Serialize(new {
	Scope = "Actual source thumbnail methods including exact-reference generic-icon handoff, BitmapHelper header/decode-size/bounded synchronous-selection functions, FileThumbnailHelper, STA pool and viewport queue. Real STA apartments; Shell/GDI, WinUI dispatcher and bitmap decoding are doubles, with image-cache call counts, dispatcher priorities and requested/selected decode mode recorded. PNG headers are synthetic, not full decoded rasters. No full PNG integrity, WinUI scheduling/SetSource latency/aspect/rendering or UI/FPS claim.",
	Sources = sources, Results = results, Passed = results.Count
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Thumbnail contracts: {results.Count} passed.");

ShellViewModel Fresh()
{
	Win32Helper.Reset(); BitmapHelper.Reset(); App.AppModel.AppWindowDPI = 1;
	UiPerformanceTrace.IsEnabled = false; UiPerformanceTrace.Records = new();
	return new();
}
ListedItem Item() => new() { ItemPath = Path.Combine(UiPerformanceTrace.FixtureRoot, "synthetic.bmp"), ItemDateModifiedReal = DateTimeOffset.UnixEpoch };
async Task CheckInvalidatedGeneric(Action<ShellViewModel, ListedItem> change, bool requireInvalidState = true)
{
	using var shell = Fresh(); using var lifetime = new CancellationTokenSource(); var item = Item();
	Win32Helper.IconHandler = request => request.Options.HasFlag(IconOptions.ReturnOnlyIfCached) ? null : [42];
	var initial = await shell.ApplyCachedThumbnailOrPreloadedIconAsync(item, lifetime.Token);
	Assert(initial.PreloadedIconImage is not null && ReferenceEquals(initial.PreloadedIconImage, item.FileImage), "Initial generic icon reference was not recorded.");
	change(shell, item);
	Assert(shell.TestCanReuse(item, lifetime.Token, initial) == !requireInvalidState, "Generic handoff state validity did not reflect the change.");
	await shell.TestLoad(item, lifetime.Token, initial);
	Assert(shell.TestIconImageCalls == 2 && shell.TestDispatcherPriorities.Count(priority => priority == Microsoft.UI.Dispatching.DispatcherQueuePriority.Low) == 1,
		"Invalid or replaced generic image suppressed conservative shared-icon application.");
	Assert(item.FileImage?.Marker == (shell.ShowThumbnails ? 42 : 200), "Conservative reapplication lost the final thumbnail/icon.");
}
static byte[] PngHeader(uint width, uint height)
{
	var header = new byte[33];
	ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
	signature.CopyTo(header);
	BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8, 4), 13);
	"IHDR"u8.CopyTo(header.AsSpan(12, 4));
	BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16, 4), width);
	BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20, 4), height);
	header[24] = 8; header[25] = 6;
	return header;
}
async Task Run(string name, Func<Task> test)
{
	await test().WaitAsync(TimeSpan.FromSeconds(15)); results.Add(new { Name = name, Passed = true }); Console.WriteLine(name);
}
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void WaitGate(ManualResetEventSlim gate) { if (!gate.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Blocked native delegate timeout."); }
static async Task AssertCanceled(Task task)
{
	try { await task.WaitAsync(TimeSpan.FromSeconds(5)); throw new InvalidOperationException("Expected cancellation."); }
	catch (OperationCanceledException) { }
}
static async Task WaitUntil(Func<bool> condition)
{
	var started = Stopwatch.GetTimestamp();
	while (!condition())
	{
		if (Stopwatch.GetElapsedTime(started).TotalSeconds > 5) throw new TimeoutException("Condition not reached.");
		await Task.Delay(5);
	}
}
