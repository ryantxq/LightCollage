using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using LightCollage.Common;
using LightCollage.Core;

/// <summary>
/// 轻拼 Core 自包含单元测试（不依赖测试框架）。
/// 运行方式见 tests/core/run-core-tests.ps1：先生成 fixtures，再用系统 csc 编译运行。
/// 参数：fixtures 目录（默认 ../fixtures）。
/// </summary>
internal static class CoreTests
{
    private static int _passed;
    private static int _failed;
    private static int _skipped;
    private static string _fixtures;
    private static string _tempRoot;
    private static int _tempCounter;
    private static readonly List<string> FailureDetails = new List<string>();

    private static readonly Color Red = Color.FromArgb(255, 255, 0, 0);
    private static readonly Color Green = Color.FromArgb(255, 0, 255, 0);
    private static readonly Color Blue = Color.FromArgb(255, 0, 0, 255);
    private static readonly Color Yellow = Color.FromArgb(255, 255, 255, 0);

    private static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch (Exception)
        {
        }

        _fixtures = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath(Path.Combine("..", "fixtures"));
        if (!Directory.Exists(_fixtures))
        {
            Console.WriteLine("FIXTURES NOT FOUND: " + _fixtures);
            Console.WriteLine("请先运行: python make_test_images.py <fixtures目录>");
            return 2;
        }

        _tempRoot = Path.Combine(Path.GetTempPath(), "LightCollageCoreTests",
            DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_tempRoot);

        Console.WriteLine("LightCollage Core Tests");
        Console.WriteLine("fixtures: " + _fixtures);
        Console.WriteLine("temp:     " + _tempRoot);
        Console.WriteLine(new string('=', 78));

        try
        {
            RunAll();
        }
        finally
        {
            try
            {
                Directory.Delete(_tempRoot, true);
            }
            catch (Exception)
            {
            }
        }

        Console.WriteLine(new string('=', 78));
        Console.WriteLine("PASSED: " + _passed + "   FAILED: " + _failed + "   SKIPPED: " + _skipped);
        if (_failed > 0)
        {
            Console.WriteLine();
            Console.WriteLine("失败列表：");
            for (int i = 0; i < FailureDetails.Count; i++)
            {
                Console.WriteLine("  - " + FailureDetails[i]);
            }
            return 1;
        }
        Console.WriteLine("ALL TESTS PASSED");
        return 0;
    }

    private static void RunAll()
    {
        // 布局
        Test("Layout/Empty", LayoutEmpty);
        Test("Layout/SingleNoScale", LayoutSingleNoScale);
        Test("Layout/HorizontalAutoMedian", LayoutHorizontalAutoMedian);
        Test("Layout/VerticalAutoMedian", LayoutVerticalAutoMedian);
        Test("Layout/HorizontalItemCoordinates", LayoutHorizontalItemCoordinates);
        Test("Layout/VerticalItemCoordinates", LayoutVerticalItemCoordinates);
        Test("Layout/AutoMedianRounding", LayoutAutoMedianRounding);
        Test("Layout/CustomBaseSize", LayoutCustomBaseSize);
        Test("Layout/CustomBaseFallback", LayoutCustomBaseFallback);
        Test("Layout/GapMarginClamp", LayoutGapMarginClamp);
        Test("Layout/MaxSideClamp", LayoutMaxSideClamp);
        Test("Layout/MaxPixelsClamp", LayoutMaxPixelsClamp);
        Test("Layout/ExtremeInputNoThrow", LayoutExtremeInputNoThrow);
        Test("Layout/SingleHugeSquareClamp", LayoutSingleHugeSquareClamp);
        Test("Layout/InvalidSizes", LayoutInvalidSizes);
        Test("Layout/InvalidOrientationFallsBack", LayoutInvalidOrientationFallsBack);
        Test("Layout/WidthModeExact", LayoutWidthModeExact);
        Test("Layout/HeightModeExact", LayoutHeightModeExact);
        Test("Layout/WidthModeVertical", LayoutWidthModeVertical);
        Test("Layout/HeightModeVertical", LayoutHeightModeVertical);
        Test("Layout/WidthModeGapZeroAdjacent", LayoutWidthModeGapZeroAdjacent);
        Test("Layout/HeightModeGapZeroAdjacent", LayoutHeightModeGapZeroAdjacent);
        Test("Layout/WidthModeSingleExact", LayoutWidthModeSingleExact);
        Test("Layout/WidthModeClampSide", LayoutWidthModeClampSide);
        Test("Layout/HeightModePixelClamp", LayoutHeightModePixelClamp);
        Test("Layout/TargetInvalidSafe", LayoutTargetInvalidSafe);

        // 网格布局（v1.1）
        Test("Layout/GridAutoCellSize", LayoutGridAutoCellSize);
        Test("Layout/GridGeometry3x3", LayoutGridGeometry3x3);
        Test("Layout/GridRowMajorOrder", LayoutGridRowMajorOrder);
        Test("Layout/GridLastRowEmpty", LayoutGridLastRowEmpty);
        Test("Layout/GridMerge2x2", LayoutGridMerge2x2);
        Test("Layout/GridMergeSpanSingleLine", LayoutGridMergeSpanSingleLine);
        Test("Layout/GridSingleCell", LayoutGridSingleCell);
        Test("Layout/GridCapacityTruncation", LayoutGridCapacityTruncation);
        Test("Layout/GridMax20x20", LayoutGridMax20x20);
        Test("Layout/GridGapZeroSeamless", LayoutGridGapZeroSeamless);
        Test("Layout/GridWidthModeExact", LayoutGridWidthModeExact);
        Test("Layout/GridHeightModeExact", LayoutGridHeightModeExact);
        Test("Layout/GridMergeWidthMode", LayoutGridMergeWidthMode);
        Test("Layout/GridSizeClamp", LayoutGridSizeClamp);
        Test("Layout/GridInvalidMergesIgnored", LayoutGridInvalidMergesIgnored);
        Test("Layout/GridClampInvalidSettings", LayoutGridClampInvalidSettings);
        Test("Layout/GridEmptyAndSingle", LayoutGridEmptyAndSingle);

        // 加载
        Test("Loader/SupportedExtensions", LoaderSupportedExtensions);
        Test("Loader/BasicEditing", LoaderBasicEditing);
        Test("Loader/NeverThrows", LoaderNeverThrows);
        Test("Loader/FakeHeicMessage", LoaderFakeHeicMessage);
        Test("Loader/NoFileLock", LoaderNoFileLock);
        Test("Loader/GifFirstFrame", LoaderGifFirstFrame);
        Test("Loader/PreviewThumbSizesAndAlpha", LoaderPreviewThumbSizesAndAlpha);
        Test("Loader/ExifOrientations", LoaderExifOrientations);
        Test("Loader/ExifEditingDimensions", LoaderExifEditingDimensions);
        Test("Loader/LoadFullExceptions", LoaderLoadFullExceptions);
        Test("Loader/WebP", LoaderWebP);

        // 合成/保存
        Test("Compose/PreviewTransparentBackground", ComposePreviewTransparentBackground);
        Test("Compose/PreviewOpaqueBackground", ComposePreviewOpaqueBackground);
        Test("Compose/PreviewSemiTransparentBackground", ComposePreviewSemiTransparentBackground);
        Test("Compose/PreviewJpegFlattensWhite", ComposePreviewJpegFlattensWhite);
        Test("Compose/PreviewScaleToMax", ComposePreviewScaleToMax);
        Test("Compose/PreviewEmptyNull", ComposePreviewEmptyNull);
        Test("Compose/ComposePngPixels", ComposeComposePngPixels);
        Test("Compose/HorizontalTwoImagesPixels", ComposeHorizontalTwoImagesPixels);
        Test("Compose/VerticalTwoImagesPixels", ComposeVerticalTwoImagesPixels);
        Test("Compose/CliCase88x24", ComposeCliCase88x24);
        Test("Compose/ComposeJpegIs24bppWhite", ComposeComposeJpegIs24bppWhite);
        Test("Compose/SavePngRoundtrip", ComposeSavePngRoundtrip);
        Test("Compose/SavePngSemiBgRoundtrip", ComposeSavePngSemiBgRoundtrip);
        Test("Compose/SaveJpegWhite", ComposeSaveJpegWhite);
        Test("Compose/SaveJpegSemiBgFlatten", ComposeSaveJpegSemiBgFlatten);
        Test("Compose/ProgressReporting", ComposeProgressReporting);
        Test("Compose/SaveDirectoryMissing", ComposeSaveDirectoryMissing);
        Test("Compose/ArgumentValidation", ComposeArgumentValidation);
        Test("Compose/PreviewMatchesSavedPng", ComposePreviewMatchesSavedPng);
        Test("Compose/GapZeroSeamWidthMode", ComposeGapZeroSeamWidthMode);
        Test("Compose/GapZeroSeamVertical", ComposeGapZeroSeamVertical);
        Test("Compose/GapZeroPreviewSeam", ComposeGapZeroPreviewSeam);
        Test("Compose/GapZeroPreviewFromBigPreviews", ComposeGapZeroPreviewFromBigPreviews);
        Test("Compose/GapZeroJpegNoBackgroundColumn", ComposeGapZeroJpegNoBackgroundColumn);

        // 网格合成（v1.1）
        Test("Compose/GridTransparentGaps", ComposeGridTransparentGaps);
        Test("Compose/GridMergeContain", ComposeGridMergeContain);
        Test("Compose/GridEmptySlotsBackground", ComposeGridEmptySlotsBackground);
        Test("Compose/GridJpegWhiteBackground", ComposeGridJpegWhiteBackground);
        Test("Compose/GridPreviewMatchesSave", ComposeGridPreviewMatchesSave);
        Test("Compose/GridCapacityOverflow", ComposeGridCapacityOverflow);

        // 设置
        Test("Settings/Defaults", SettingsDefaults);
        Test("Settings/RoundTrip", SettingsRoundTrip);
        Test("Settings/CorruptAndClamp", SettingsCorruptAndClamp);
        Test("Settings/DefaultPaths", SettingsDefaultPaths);
        Test("Settings/OutputSizeRoundTrip", SettingsOutputSizeRoundTrip);
        Test("Settings/OldConfigDefaults", SettingsOldConfigDefaults);
        Test("Settings/OutputSizeClamp", SettingsOutputSizeClamp);
        Test("Settings/SaveFailureSilent", SettingsSaveFailureSilent);

        // 网格设置（v1.1）
        Test("Settings/GridDefaults", SettingsGridDefaults);
        Test("Settings/GridRoundTrip", SettingsGridRoundTrip);
        Test("Settings/GridClampAndCorrupt", SettingsGridClampAndCorrupt);
        Test("Settings/GridOldConfigDefaults", SettingsGridOldConfigDefaults);
    }

    // ---------------------------------------------------------------- 布局

    private static void LayoutEmpty()
    {
        CollageLayout layout = LayoutEngine.Compute(new List<ImageSize>(), new CollageSettings());
        AreEqual(0, layout.CanvasWidth, "空列表画布宽");
        AreEqual(0, layout.CanvasHeight, "空列表画布高");
        AreEqual(0, layout.BaseSize, "空列表基准");
        AreEqual(0, layout.Count, "空列表项数");
        AssertTrue(!layout.WasClamped, "空列表不应标记 clamp");
        AssertTrue(layout.Items != null, "Items 不应为 null");
    }

    private static void LayoutSingleNoScale()
    {
        List<ImageSize> sizes = Sizes(800, 600);
        CollageLayout layout = LayoutEngine.Compute(sizes, new CollageSettings());
        AreEqual(832, layout.CanvasWidth, "单图画布宽 = 2*16+800");
        AreEqual(632, layout.CanvasHeight, "单图画布高 = 2*16+600");
        AreEqual(600, layout.BaseSize, "单图自动基准=原图高");
        AreEqual(1, layout.Count, "项数");
        AreEqual(16, layout.Items[0].X, "item.X");
        AreEqual(16, layout.Items[0].Y, "item.Y");
        AreEqual(800, layout.Items[0].Width, "item.W");
        AreEqual(600, layout.Items[0].Height, "item.H");
        AreEqual(0, layout.Items[0].Index, "item.Index");
        AssertTrue(!layout.WasClamped, "不应 clamp");
    }

    private static void LayoutHorizontalAutoMedian()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(400, 300));
        sizes.Add(new ImageSize(300, 600));
        sizes.Add(new ImageSize(500, 250));

        CollageLayout layout = LayoutEngine.Compute(sizes, new CollageSettings());
        AreEqual(300, layout.BaseSize, "高度中位数=300");
        AreEqual(400, layout.Items[0].Width, "W1");
        AreEqual(150, layout.Items[1].Width, "W2");
        AreEqual(600, layout.Items[2].Width, "W3");
        AreEqual(1206, layout.CanvasWidth, "画布宽 2*16+1150+2*12");
        AreEqual(332, layout.CanvasHeight, "画布高 2*16+300");
        AreEqual(16, layout.Items[0].X, "X1");
        AreEqual(428, layout.Items[1].X, "X2");
        AreEqual(590, layout.Items[2].X, "X3");
        AreEqual(16, layout.Items[1].Y, "Y");
        AreEqual(300, layout.Items[1].Height, "统一高度");
        AreEqual(300, layout.Items[2].Height, "统一高度2");
    }

    private static void LayoutVerticalAutoMedian()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(400, 300));
        sizes.Add(new ImageSize(300, 600));

        CollageSettings settings = new CollageSettings();
        settings.Orientation = CollageOrientation.Vertical;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(350, layout.BaseSize, "宽度中位数=(300+400)/2=350");
        AreEqual(263, layout.Items[0].Height, "H1=round(300*350/400)=262.5->263");
        AreEqual(700, layout.Items[1].Height, "H2=round(600*350/300)=700");
        AreEqual(350, layout.Items[0].Width, "统一宽度");
        AreEqual(382, layout.CanvasWidth, "画布宽 2*16+350");
        AreEqual(1007, layout.CanvasHeight, "画布高 2*16+263+700+12");
        AreEqual(16, layout.Items[0].Y, "Y1");
        AreEqual(291, layout.Items[1].Y, "Y2=16+263+12");
    }

    private static void LayoutHorizontalItemCoordinates()
    {
        // 横向：统一高度，所有图片 Y=margin，沿 X 轴顺序向右排列（回归：曾经误用纵向坐标）
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(100, 200));
        sizes.Add(new ImageSize(50, 100));
        sizes.Add(new ImageSize(200, 400));
        CollageLayout layout = LayoutEngine.Compute(sizes, new CollageSettings());
        AreEqual(200, layout.BaseSize, "统一高度=高度中位数 200");
        AreEqual(100, layout.Items[0].Width, "W1");
        AreEqual(100, layout.Items[1].Width, "W2");
        AreEqual(100, layout.Items[2].Width, "W3");
        AreEqual(16, layout.Items[0].Y, "横向第 1 张 Y=margin");
        AreEqual(16, layout.Items[1].Y, "横向第 2 张 Y=margin");
        AreEqual(16, layout.Items[2].Y, "横向第 3 张 Y=margin");
        AreEqual(16, layout.Items[0].X, "X1=margin");
        AreEqual(128, layout.Items[1].X, "X2=16+100+12");
        AreEqual(240, layout.Items[2].X, "X3=128+100+12");
        AreEqual(356, layout.CanvasWidth, "画布宽 2*16+300+2*12");
        AreEqual(232, layout.CanvasHeight, "画布高 2*16+200");
        for (int i = 1; i < layout.Count; i++)
        {
            AssertTrue(layout.Items[i].X >= layout.Items[i - 1].X + layout.Items[i - 1].Width,
                "横向第 " + i + " 张图应在上一张右侧");
        }
    }

    private static void LayoutVerticalItemCoordinates()
    {
        // 纵向：统一宽度，所有图片 X=margin，沿 Y 轴顺序向下排列
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(200, 100));
        sizes.Add(new ImageSize(400, 50));
        sizes.Add(new ImageSize(100, 200));
        CollageSettings settings = new CollageSettings();
        settings.Orientation = CollageOrientation.Vertical;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(200, layout.BaseSize, "统一宽度=宽度中位数 200");
        AreEqual(100, layout.Items[0].Height, "H1=round(100*200/200)");
        AreEqual(25, layout.Items[1].Height, "H2=round(50*200/400)");
        AreEqual(400, layout.Items[2].Height, "H3=round(200*200/100)");
        AreEqual(16, layout.Items[0].X, "纵向第 1 张 X=margin");
        AreEqual(16, layout.Items[1].X, "纵向第 2 张 X=margin");
        AreEqual(16, layout.Items[2].X, "纵向第 3 张 X=margin");
        AreEqual(16, layout.Items[0].Y, "Y1=margin");
        AreEqual(128, layout.Items[1].Y, "Y2=16+100+12");
        AreEqual(165, layout.Items[2].Y, "Y3=128+25+12");
        AreEqual(232, layout.CanvasWidth, "画布宽 2*16+200");
        AreEqual(581, layout.CanvasHeight, "画布高 2*16+525+2*12");
        for (int i = 1; i < layout.Count; i++)
        {
            AssertTrue(layout.Items[i].Y >= layout.Items[i - 1].Y + layout.Items[i - 1].Height,
                "纵向第 " + i + " 张图应在上一张下方");
        }
    }

    private static void LayoutAutoMedianRounding()
    {
        List<ImageSize> even = new List<ImageSize>();
        even.Add(new ImageSize(10, 301));
        even.Add(new ImageSize(10, 302));
        AreEqual(302, LayoutEngine.GetAutoBaseSize(even, CollageOrientation.Horizontal),
            "偶数中位数 301.5 四舍五入=302");

        List<ImageSize> odd = new List<ImageSize>();
        odd.Add(new ImageSize(10, 100));
        odd.Add(new ImageSize(10, 50));
        odd.Add(new ImageSize(10, 200));
        AreEqual(100, LayoutEngine.GetAutoBaseSize(odd, CollageOrientation.Horizontal), "奇数中位数");

        List<ImageSize> widths = new List<ImageSize>();
        widths.Add(new ImageSize(301, 10));
        widths.Add(new ImageSize(302, 10));
        widths.Add(new ImageSize(999, 10));
        AreEqual(302, LayoutEngine.GetAutoBaseSize(widths, CollageOrientation.Vertical), "纵向宽度中位数");

        AreEqual(0, LayoutEngine.GetAutoBaseSize(new List<ImageSize>(), CollageOrientation.Horizontal),
            "空列表自动基准=0");
    }

    private static void LayoutCustomBaseSize()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(400, 300));
        sizes.Add(new ImageSize(300, 600));
        CollageSettings settings = new CollageSettings();
        settings.AutoBaseSize = false;
        settings.BaseSize = 100;

        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(100, layout.BaseSize, "自定义基准");
        AreEqual(133, layout.Items[0].Width, "W1=round(400*100/300)=133");
        AreEqual(50, layout.Items[1].Width, "W2=round(300*100/600)=50");
        AreEqual(227, layout.CanvasWidth, "画布宽 2*16+183+12");
        AreEqual(132, layout.CanvasHeight, "画布高 2*16+100");
        AreEqual(161, layout.Items[1].X, "X2=16+133+12");
        AreEqual(100, layout.Items[0].Height, "统一高度=100");
    }

    private static void LayoutCustomBaseFallback()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(400, 300));
        sizes.Add(new ImageSize(300, 600));
        CollageSettings settings = new CollageSettings();
        settings.AutoBaseSize = false;
        settings.BaseSize = 0; // 非法，应退回自动值
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(450, layout.BaseSize, "非法自定义基准退回自动中位数 (300+600)/2=450");
    }

    private static void LayoutGapMarginClamp()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(10, 10));
        sizes.Add(new ImageSize(10, 10));
        CollageSettings settings = new CollageSettings();
        settings.Gap = 500;   // clamp 到 200
        settings.Margin = -10; // clamp 到 0
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(220, layout.CanvasWidth, "gap=200 margin=0 => 0+10+10+200");
        AreEqual(10, layout.CanvasHeight, "margin=0 => 高度 10");
        AreEqual(0, layout.Items[0].X, "margin=0");
    }

    private static void LayoutMaxSideClamp()
    {
        List<ImageSize> sizes = Sizes(40000, 3000);
        CollageLayout layout = LayoutEngine.Compute(sizes, new CollageSettings());
        AssertTrue(layout.WasClamped, "宽图应触发单边 clamp");
        AssertTrue(layout.CanvasWidth <= LayoutEngine.DefaultMaxSide, "画布宽不超过 32000，实际 " + layout.CanvasWidth);
        AssertTrue(layout.CanvasHeight <= LayoutEngine.DefaultMaxSide, "画布高不超过 32000");
        AssertTrue(layout.BaseSize < 3000 && layout.BaseSize >= 1, "基准被缩小，实际 " + layout.BaseSize);
        double ratio = (double)(layout.CanvasWidth - 32) / (double)(layout.CanvasHeight - 32);
        double expected = 40000.0 / 3000.0;
        AssertTrue(Math.Abs(ratio - expected) / expected < 0.01,
            "缩放后应保持宽高比，实际 " + ratio.ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static void LayoutMaxPixelsClamp()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        for (int i = 0; i < 10; i++)
        {
            sizes.Add(new ImageSize(1000, 1000));
        }
        CollageSettings settings = new CollageSettings();
        settings.Gap = 0;
        settings.Margin = 0;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings, 100000, 32000);
        AssertTrue(layout.WasClamped, "应触发像素上限 clamp");
        AssertTrue((long)layout.CanvasWidth * layout.CanvasHeight <= 100000,
            "像素数应 <= 100000，实际 " + layout.CanvasWidth + "x" + layout.CanvasHeight);
        AreEqual(10, layout.Count, "项数不变");
        AreEqual(100, layout.BaseSize, "10 张 1000x1000 收缩到基准 100");
        AreEqual(1000, layout.CanvasWidth, "画布宽 10*100");
        AreEqual(100, layout.CanvasHeight, "画布高 100");
    }

    private static void LayoutExtremeInputNoThrow()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        for (int i = 0; i < 200; i++)
        {
            sizes.Add(new ImageSize(10, 100000));
        }
        CollageLayout layout = LayoutEngine.Compute(sizes, new CollageSettings());
        AreEqual(200, layout.Count, "项数");
        AssertTrue(layout.WasClamped, "极端尺寸应 clamp");
        AssertTrue(layout.CanvasWidth <= LayoutEngine.DefaultMaxSide, "宽 <= 32000，实际 " + layout.CanvasWidth);
        AssertTrue(layout.CanvasHeight <= LayoutEngine.DefaultMaxSide, "高 <= 32000，实际 " + layout.CanvasHeight);
        AssertTrue((long)layout.CanvasWidth * layout.CanvasHeight <= LayoutEngine.DefaultMaxPixels,
            "像素 <= 8000 万，实际 " + ((long)layout.CanvasWidth * layout.CanvasHeight));
    }

    private static void LayoutSingleHugeSquareClamp()
    {
        List<ImageSize> sizes = Sizes(20000, 20000);
        CollageLayout layout = LayoutEngine.Compute(sizes, new CollageSettings());
        AssertTrue(layout.WasClamped, "正方形大图应 clamp");
        AreEqual(layout.CanvasWidth, layout.CanvasHeight, "单图应保持正方形");
        AssertTrue(layout.CanvasWidth <= LayoutEngine.DefaultMaxSide, "单边 <= 32000");
        AssertTrue((long)layout.CanvasWidth * layout.CanvasHeight <= LayoutEngine.DefaultMaxPixels,
            "像素 <= 8000 万");
    }

    private static void LayoutInvalidSizes()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(0, 0));
        sizes.Add(new ImageSize(-5, -5));
        CollageLayout layout = LayoutEngine.Compute(sizes, new CollageSettings());
        AreEqual(2, layout.Count, "无效尺寸仍保留项数");
        for (int i = 0; i < layout.Count; i++)
        {
            AssertTrue(layout.Items[i].Width >= 1, "无效宽被夹到 >=1");
            AssertTrue(layout.Items[i].Height >= 1, "无效高被夹到 >=1");
        }
        AssertTrue(layout.CanvasWidth >= 1 && layout.CanvasHeight >= 1, "画布尺寸有效");
    }

    private static void LayoutInvalidOrientationFallsBack()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(100, 50));
        sizes.Add(new ImageSize(100, 50));
        CollageSettings settings = new CollageSettings();
        settings.Orientation = (CollageOrientation)99;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(50, layout.BaseSize, "非法枚举按横向处理");
        AreEqual(244, layout.CanvasWidth, "横向布局 2*16+200+12");
    }

    // ------------------------------------------------ 输出尺寸模式（第 2 轮迭代）

    private static CollageSettings OutputModeSettings(OutputSizeMode mode, int target)
    {
        CollageSettings settings = new CollageSettings();
        settings.OutputSizeMode = mode;
        settings.OutputTarget = target;
        return settings;
    }

    private static void LayoutWidthModeExact()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(400, 300));
        sizes.Add(new ImageSize(300, 600));
        // 自然布局（Auto）：869x482，基准 450
        CollageLayout natural = LayoutEngine.Compute(sizes, new CollageSettings());
        AreEqual(869, natural.CanvasWidth, "自然布局宽");
        AreEqual(482, natural.CanvasHeight, "自然布局高");
        AreEqual(450, natural.BaseSize, "自然基准");

        CollageSettings settings = OutputModeSettings(OutputSizeMode.Width, 500);
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(500, layout.CanvasWidth, "Width 模式画布宽必须精确等于目标 500");
        int expectedHeight = (int)Math.Round(482.0 * 500.0 / 869.0, MidpointRounding.AwayFromZero);
        AreEqual(expectedHeight, layout.CanvasHeight, "高度按比例四舍五入");
        int expectedBase = (int)Math.Round(450.0 * 500.0 / 869.0, MidpointRounding.AwayFromZero);
        AreEqual(expectedBase, layout.BaseSize, "BaseSize 同步缩放");
        AssertTrue(!layout.WasClamped, "未超限不应 clamp");
        AreEqual(2, layout.Count, "项数不变");

        double naturalRatio = 482.0 / 869.0;
        double scaledRatio = (double)layout.CanvasHeight / (double)layout.CanvasWidth;
        AssertTrue(Math.Abs(naturalRatio - scaledRatio) < 0.002,
            "最终比例应与自然布局一致（比例误差 " + Math.Abs(naturalRatio - scaledRatio).ToString("0.0000", CultureInfo.InvariantCulture) + "）");
        AssertTrue(layout.Items[1].X > layout.Items[0].X + layout.Items[0].Width - 1, "图片顺序与相对位置保留");
        int scaledGap = layout.Items[1].X - (layout.Items[0].X + layout.Items[0].Width);
        int expectedGap = (int)Math.Round(12.0 * 500.0 / 869.0, MidpointRounding.AwayFromZero);
        AssertTrue(Math.Abs(scaledGap - expectedGap) <= 1, "间距随比例缩放，实际 " + scaledGap);
        LayoutItem last = layout.Items[1];
        AssertTrue(last.X + last.Width <= layout.CanvasWidth - 1, "右侧外边距应保留（最后一张不贴边）");
    }

    private static void LayoutHeightModeExact()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(400, 300));
        sizes.Add(new ImageSize(300, 600));
        CollageSettings settings = OutputModeSettings(OutputSizeMode.Height, 300);
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(300, layout.CanvasHeight, "Height 模式画布高必须精确等于目标 300");
        int expectedWidth = (int)Math.Round(869.0 * 300.0 / 482.0, MidpointRounding.AwayFromZero);
        AreEqual(expectedWidth, layout.CanvasWidth, "宽度按比例四舍五入");
        int expectedBase = (int)Math.Round(450.0 * 300.0 / 482.0, MidpointRounding.AwayFromZero);
        AreEqual(expectedBase, layout.BaseSize, "BaseSize 同步缩放");
        AssertTrue(!layout.WasClamped, "未超限不应 clamp");
        double naturalRatio = 482.0 / 869.0;
        double scaledRatio = (double)layout.CanvasHeight / (double)layout.CanvasWidth;
        AssertTrue(Math.Abs(naturalRatio - scaledRatio) < 0.003, "最终比例应与自然布局一致");
    }

    private static void LayoutWidthModeVertical()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(400, 300));
        sizes.Add(new ImageSize(300, 600));
        CollageSettings naturalSettings = new CollageSettings();
        naturalSettings.Orientation = CollageOrientation.Vertical;
        CollageLayout natural = LayoutEngine.Compute(sizes, naturalSettings);
        AreEqual(382, natural.CanvasWidth, "纵向自然布局宽");
        AreEqual(1007, natural.CanvasHeight, "纵向自然布局高");

        CollageSettings settings = OutputModeSettings(OutputSizeMode.Width, 200);
        settings.Orientation = CollageOrientation.Vertical;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(200, layout.CanvasWidth, "纵向 Width 模式画布宽=目标 200");
        int expectedHeight = (int)Math.Round(1007.0 * 200.0 / 382.0, MidpointRounding.AwayFromZero);
        AreEqual(expectedHeight, layout.CanvasHeight, "高度按比例四舍五入");
        int expectedMargin = (int)Math.Round(16.0 * 200.0 / 382.0, MidpointRounding.AwayFromZero);
        AreEqual(expectedMargin, layout.Items[0].X, "纵向统一 X=缩放后的 margin");
        AssertTrue(!layout.WasClamped, "未超限不应 clamp");
    }

    private static void LayoutHeightModeVertical()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(400, 300));
        sizes.Add(new ImageSize(300, 600));
        CollageSettings settings = OutputModeSettings(OutputSizeMode.Height, 600);
        settings.Orientation = CollageOrientation.Vertical;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(600, layout.CanvasHeight, "纵向 Height 模式画布高=目标 600");
        int expectedWidth = (int)Math.Round(382.0 * 600.0 / 1007.0, MidpointRounding.AwayFromZero);
        AreEqual(expectedWidth, layout.CanvasWidth, "宽度按比例四舍五入");
        AssertTrue(!layout.WasClamped, "未超限不应 clamp");
    }

    private static void LayoutWidthModeGapZeroAdjacent()
    {
        // gap=0 + Width 模式：边界取整必须让相邻图片坐标严格相接，不能出现 1px 缝
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(400, 300));
        sizes.Add(new ImageSize(300, 600));
        sizes.Add(new ImageSize(500, 250));
        CollageSettings settings = OutputModeSettings(OutputSizeMode.Width, 360);
        settings.Gap = 0;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(360, layout.CanvasWidth, "画布宽=目标 360");
        AreEqual(3, layout.Count, "项数");
        for (int i = 1; i < layout.Count; i++)
        {
            AreEqual(layout.Items[i - 1].X + layout.Items[i - 1].Width, layout.Items[i].X,
                "gap=0 第 " + (i + 1) + " 张图 X 必须等于上一张右边界（无缝）");
            AreEqual(layout.Items[0].Y, layout.Items[i].Y, "横向所有图片 Y 相同");
            AreEqual(layout.Items[0].Height, layout.Items[i].Height, "横向统一高度");
        }
        LayoutItem last = layout.Items[layout.Count - 1];
        AssertTrue(last.X + last.Width <= layout.CanvasWidth, "最后一张不越界");
        for (int i = 0; i < layout.Count; i++)
        {
            AssertTrue(layout.Items[i].Width >= 1 && layout.Items[i].Height >= 1, "图片矩形有效");
        }
    }

    private static void LayoutHeightModeGapZeroAdjacent()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(400, 300));
        sizes.Add(new ImageSize(300, 600));
        CollageSettings settings = OutputModeSettings(OutputSizeMode.Height, 500);
        settings.Orientation = CollageOrientation.Vertical;
        settings.Gap = 0;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(500, layout.CanvasHeight, "画布高=目标 500");
        for (int i = 1; i < layout.Count; i++)
        {
            AreEqual(layout.Items[i - 1].Y + layout.Items[i - 1].Height, layout.Items[i].Y,
                "gap=0 第 " + (i + 1) + " 张图 Y 必须等于上一张下边界（无缝）");
            AreEqual(layout.Items[0].X, layout.Items[i].X, "纵向所有图片 X 相同");
            AreEqual(layout.Items[0].Width, layout.Items[i].Width, "纵向统一宽度");
        }
    }

    private static void LayoutWidthModeSingleExact()
    {
        List<ImageSize> sizes = Sizes(200, 100);
        CollageSettings settings = OutputModeSettings(OutputSizeMode.Width, 464);
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        // 自然 232x132，放大 2 倍
        AreEqual(464, layout.CanvasWidth, "单图固定宽度=464");
        AreEqual(264, layout.CanvasHeight, "单图高度=132*2=264");
        AreEqual(200, layout.BaseSize, "基准 100*2=200");
        AreEqual(32, layout.Items[0].X, "左边距 16*2=32");
        AreEqual(32, layout.Items[0].Y, "上边距 16*2=32");
        AreEqual(400, layout.Items[0].Width, "图片宽 200*2=400");
        AreEqual(200, layout.Items[0].Height, "图片高 100*2=200");
        AssertTrue(!layout.WasClamped, "不应 clamp");
    }

    private static void LayoutWidthModeClampSide()
    {
        // 纵向 3 张 100x4000：自然 132x12056；指定宽 30000 会让高度达 274 万，必须自动缩小目标
        List<ImageSize> sizes = new List<ImageSize>();
        for (int i = 0; i < 3; i++)
        {
            sizes.Add(new ImageSize(100, 4000));
        }
        CollageSettings settings = OutputModeSettings(OutputSizeMode.Width, 30000);
        settings.Orientation = CollageOrientation.Vertical;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AssertTrue(layout.WasClamped, "超单边上限应 clamp");
        AssertTrue(layout.CanvasWidth <= LayoutEngine.DefaultMaxSide,
            "画布宽 <= 32000，实际 " + layout.CanvasWidth);
        AssertTrue(layout.CanvasHeight <= LayoutEngine.DefaultMaxSide,
            "画布高 <= 32000，实际 " + layout.CanvasHeight);
        AssertTrue((long)layout.CanvasWidth * layout.CanvasHeight <= LayoutEngine.DefaultMaxPixels,
            "像素 <= 8000 万，实际 " + ((long)layout.CanvasWidth * layout.CanvasHeight));
        AssertTrue(layout.CanvasWidth < 30000, "目标宽度被自动缩小，实际 " + layout.CanvasWidth);
        AreEqual(3, layout.Count, "项数不变");
    }

    private static void LayoutHeightModePixelClamp()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        for (int i = 0; i < 10; i++)
        {
            sizes.Add(new ImageSize(1000, 1000));
        }
        CollageSettings settings = OutputModeSettings(OutputSizeMode.Height, 32000);
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AssertTrue(layout.WasClamped, "超像素上限应 clamp");
        AssertTrue((long)layout.CanvasWidth * layout.CanvasHeight <= LayoutEngine.DefaultMaxPixels,
            "像素 <= 8000 万，实际 " + ((long)layout.CanvasWidth * layout.CanvasHeight)
            + "（" + layout.CanvasWidth + "x" + layout.CanvasHeight + "）");
        AssertTrue(layout.CanvasWidth <= LayoutEngine.DefaultMaxSide, "单边 <= 32000");
        AssertTrue(layout.CanvasHeight <= LayoutEngine.DefaultMaxSide, "单边 <= 32000");
        AreEqual(10, layout.Count, "项数不变");
        AssertTrue(layout.CanvasHeight < 32000, "目标高度被自动缩小");
    }

    private static void LayoutTargetInvalidSafe()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(400, 300));
        sizes.Add(new ImageSize(300, 600));
        CollageLayout autoLayout = LayoutEngine.Compute(sizes, new CollageSettings());

        CollageSettings zeroTarget = OutputModeSettings(OutputSizeMode.Width, 0);
        CollageLayout zeroLayout = LayoutEngine.Compute(sizes, zeroTarget);
        AreEqual(autoLayout.CanvasWidth, zeroLayout.CanvasWidth, "目标<1 安全退回 Auto（宽）");
        AreEqual(autoLayout.CanvasHeight, zeroLayout.CanvasHeight, "目标<1 安全退回 Auto（高）");
        AreEqual(autoLayout.BaseSize, zeroLayout.BaseSize, "目标<1 安全退回 Auto（基准）");

        CollageSettings negativeTarget = OutputModeSettings(OutputSizeMode.Height, -5);
        CollageLayout negativeLayout = LayoutEngine.Compute(sizes, negativeTarget);
        AreEqual(autoLayout.CanvasWidth, negativeLayout.CanvasWidth, "负目标安全退回 Auto");

        CollageSettings invalidMode = OutputModeSettings((OutputSizeMode)99, 500);
        CollageLayout invalidLayout = LayoutEngine.Compute(sizes, invalidMode);
        AreEqual(autoLayout.CanvasWidth, invalidLayout.CanvasWidth, "非法模式按 Auto 处理");

        CollageLayout empty = LayoutEngine.Compute(new List<ImageSize>(), OutputModeSettings(OutputSizeMode.Width, 500));
        AreEqual(0, empty.CanvasWidth, "空列表 Width 模式安全返回 0x0");
        AreEqual(0, empty.CanvasHeight, "空列表 Width 模式高 0");
        AreEqual(0, empty.Count, "空列表项数 0");
    }

    // ---------------------------------------------------------------- 网格布局（v1.1）

    private static CollageSettings GridSettings(int rows, int columns, int rowGap, int columnGap, int margin)
    {
        CollageSettings settings = new CollageSettings();
        settings.Orientation = CollageOrientation.Grid;
        settings.GridRows = rows;
        settings.GridColumns = columns;
        settings.RowGap = rowGap;
        settings.ColumnGap = columnGap;
        settings.Margin = margin;
        return settings;
    }

    private static List<ImageSize> RepeatedSizes(int count, int width, int height)
    {
        List<ImageSize> sizes = new List<ImageSize>(count);
        for (int i = 0; i < count; i++)
        {
            sizes.Add(new ImageSize(width, height));
        }
        return sizes;
    }

    private static void AssertGridInfo(CollageLayout layout, int rows, int columns, int cellWidth,
                                       int cellHeight, int rowGap, int columnGap, int margin, int capacity)
    {
        AssertTrue(layout.IsGrid, "应标记为网格布局");
        AreEqual(rows, layout.GridRows, "GridRows");
        AreEqual(columns, layout.GridColumns, "GridColumns");
        AreEqual(cellWidth, layout.CellWidth, "CellWidth");
        AreEqual(cellHeight, layout.CellHeight, "CellHeight");
        AreEqual(rowGap, layout.RowGap, "RowGap");
        AreEqual(columnGap, layout.ColumnGap, "ColumnGap");
        AreEqual(margin, layout.GridMargin, "GridMargin");
        AreEqual(capacity, layout.Capacity, "Capacity");
        AreEqual(cellWidth, layout.BaseSize, "网格 BaseSize=CellWidth");
    }

    private static void LayoutGridAutoCellSize()
    {
        List<ImageSize> odd = new List<ImageSize>();
        odd.Add(new ImageSize(100, 50));
        odd.Add(new ImageSize(300, 60));
        odd.Add(new ImageSize(200, 70));
        ImageSize oddCell = LayoutEngine.GetAutoGridCellSize(odd);
        AreEqual(200, oddCell.Width, "宽度中位数 200");
        AreEqual(60, oddCell.Height, "高度中位数 60");

        List<ImageSize> even = new List<ImageSize>();
        even.Add(new ImageSize(10, 301));
        even.Add(new ImageSize(12, 302));
        ImageSize evenCell = LayoutEngine.GetAutoGridCellSize(even);
        AreEqual(11, evenCell.Width, "偶数宽度中位数 (10+12)/2=11");
        AreEqual(302, evenCell.Height, "偶数高度中位数 301.5 四舍五入=302");

        ImageSize emptyCell = LayoutEngine.GetAutoGridCellSize(new List<ImageSize>());
        AreEqual(0, emptyCell.Width, "空列表自动单元格宽 0");
        AreEqual(0, emptyCell.Height, "空列表自动单元格高 0");
        AssertTrue(LayoutEngine.GetAutoGridCellSize(null) != null, "null 列表安全返回");

        List<ImageSize> invalid = new List<ImageSize>();
        invalid.Add(new ImageSize(0, -5));
        invalid.Add(new ImageSize(-1, 0));
        ImageSize invalidCell = LayoutEngine.GetAutoGridCellSize(invalid);
        AreEqual(1, invalidCell.Width, "非法宽度夹到 1");
        AreEqual(1, invalidCell.Height, "非法高度夹到 1");
    }

    private static void LayoutGridGeometry3x3()
    {
        CollageLayout layout = LayoutEngine.Compute(RepeatedSizes(9, 100, 100),
            GridSettings(3, 3, 12, 12, 16));
        AreEqual(356, layout.CanvasWidth, "画布宽 2*16+3*100+2*12");
        AreEqual(356, layout.CanvasHeight, "画布高 2*16+3*100+2*12");
        AssertGridInfo(layout, 3, 3, 100, 100, 12, 12, 16, 9);
        AreEqual(9, layout.Count, "9 张图片全部放入");
        AssertTrue(!layout.WasClamped, "普通网格不应 clamp");
        for (int i = 0; i < layout.Count; i++)
        {
            AreEqual(i, layout.Items[i].Index, "LayoutItem.Index=图片下标");
            AreEqual(100, layout.Items[i].Width, "单元格宽");
            AreEqual(100, layout.Items[i].Height, "单元格高");
        }
        AreEqual(16, layout.Items[0].X, "x(0)=margin");
        AreEqual(16, layout.Items[0].Y, "y(0)=margin");
        AreEqual(128, layout.Items[1].X, "x(1)=16+100+12");
        AreEqual(240, layout.Items[2].X, "x(2)=16+2*(100+12)");
        AreEqual(16, layout.Items[3].X, "第 2 行第 1 列 X");
        AreEqual(128, layout.Items[3].Y, "y(1)=16+100+12");
        AreEqual(128, layout.Items[4].X, "第 2 行第 2 列 X");
        AreEqual(240, layout.Items[8].X, "第 3 行第 3 列 X");
        AreEqual(240, layout.Items[8].Y, "第 3 行第 3 列 Y");
    }

    private static void LayoutGridRowMajorOrder()
    {
        CollageLayout layout = LayoutEngine.Compute(RepeatedSizes(4, 100, 100),
            GridSettings(2, 2, 12, 12, 16));
        AreEqual(4, layout.Count, "4 张图放入 2×2");
        for (int i = 0; i < layout.Count; i++)
        {
            AreEqual(i, layout.Items[i].Index, "Index 与行优先顺序一致");
        }
        AreEqual(16, layout.Items[0].X, "第 1 张 X");
        AreEqual(16, layout.Items[0].Y, "第 1 张 Y");
        AreEqual(128, layout.Items[1].X, "第 2 张 X（行优先：向右）");
        AreEqual(16, layout.Items[1].Y, "第 2 张 Y");
        AreEqual(16, layout.Items[2].X, "第 3 张 X（换行）");
        AreEqual(128, layout.Items[2].Y, "第 3 张 Y");
        AreEqual(128, layout.Items[3].X, "第 4 张 X");
        AreEqual(128, layout.Items[3].Y, "第 4 张 Y");
        AreEqual(244, layout.CanvasWidth, "画布宽 2*16+2*100+12");
        AreEqual(244, layout.CanvasHeight, "画布高 2*16+2*100+12");
    }

    private static void LayoutGridLastRowEmpty()
    {
        // 3×3 只放 7 张图：第 3 行最后两格留空，几何仍按完整网格
        CollageLayout layout = LayoutEngine.Compute(RepeatedSizes(7, 100, 100),
            GridSettings(3, 3, 12, 12, 16));
        AreEqual(7, layout.Count, "只产出 7 个 LayoutItem");
        AreEqual(9, layout.Capacity, "容量仍为 9");
        AreEqual(356, layout.CanvasWidth, "画布按完整网格计算");
        AreEqual(356, layout.CanvasHeight, "画布高按完整网格计算");
        AreEqual(16, layout.Items[6].X, "第 7 张在第 3 行第 1 列");
        AreEqual(240, layout.Items[6].Y, "第 7 张 Y");
    }

    private static void LayoutGridMerge2x2()
    {
        CollageSettings settings = GridSettings(3, 3, 12, 12, 16);
        settings.Merges.Add(new GridRegion(0, 0, 2, 2));
        CollageLayout layout = LayoutEngine.Compute(RepeatedSizes(7, 100, 100), settings);
        AreEqual(6, layout.Capacity, "2×2 合并后容量 9-3=6");
        AreEqual(6, layout.Count, "placed=min(7,6)=6");
        AreEqual(356, layout.CanvasWidth, "画布宽不受合并影响");
        AreEqual(16, layout.Items[0].X, "大格 X=margin");
        AreEqual(16, layout.Items[0].Y, "大格 Y=margin");
        AreEqual(212, layout.Items[0].Width, "大格宽 2*100+12（含跨越的内部列间距）");
        AreEqual(212, layout.Items[0].Height, "大格高 2*100+12（含跨越的内部行间距）");
        AreEqual(0, layout.Items[0].Index, "大格放第 1 张图");
        AreEqual(240, layout.Items[1].X, "区域 2 X=x(2)");
        AreEqual(16, layout.Items[1].Y, "区域 2 Y");
        AreEqual(240, layout.Items[2].X, "区域 3 X");
        AreEqual(128, layout.Items[2].Y, "区域 3 Y");
        AreEqual(16, layout.Items[3].X, "区域 4 X");
        AreEqual(240, layout.Items[3].Y, "区域 4 Y");
        AreEqual(128, layout.Items[4].X, "区域 5 X");
        AreEqual(240, layout.Items[4].Y, "区域 5 Y");
        AreEqual(240, layout.Items[5].X, "区域 6 X");
        AreEqual(240, layout.Items[5].Y, "区域 6 Y");
    }

    private static void LayoutGridMergeSpanSingleLine()
    {
        // 1×3 横向合并：宽度包含跨越的列间距，只放 1 张图
        CollageSettings horizontal = GridSettings(2, 3, 12, 12, 16);
        horizontal.Merges.Add(new GridRegion(0, 0, 1, 3));
        CollageLayout h = LayoutEngine.Compute(RepeatedSizes(5, 100, 100), horizontal);
        AreEqual(4, h.Capacity, "1×3 合并后容量 6-2=4");
        AreEqual(4, h.Count, "只放入 4 张图");
        AreEqual(356, h.CanvasWidth, "画布宽 2*16+3*100+2*12");
        AreEqual(244, h.CanvasHeight, "画布高 2*16+2*100+12");
        AreEqual(324, h.Items[0].Width, "大格宽 3*100+2*12");
        AreEqual(100, h.Items[0].Height, "大格高");
        AreEqual(16, h.Items[1].X, "第 2 张 X");
        AreEqual(128, h.Items[1].Y, "第 2 张 Y=16+100+12");

        // 3×1 纵向合并
        CollageSettings vertical = GridSettings(3, 2, 12, 12, 16);
        vertical.Merges.Add(new GridRegion(0, 0, 3, 1));
        CollageLayout v = LayoutEngine.Compute(RepeatedSizes(4, 100, 100), vertical);
        AreEqual(4, v.Capacity, "3×1 合并后容量 6-2=4");
        AreEqual(244, v.CanvasWidth, "画布宽 2*16+2*100+12");
        AreEqual(356, v.CanvasHeight, "画布高 2*16+3*100+2*12");
        AreEqual(100, v.Items[0].Width, "大格宽");
        AreEqual(324, v.Items[0].Height, "大格高 3*100+2*12");
        AreEqual(128, v.Items[1].X, "第 2 张 X=16+100+12");
        AreEqual(16, v.Items[1].Y, "第 2 张 Y");
    }

    private static void LayoutGridSingleCell()
    {
        CollageLayout layout = LayoutEngine.Compute(RepeatedSizes(3, 100, 100),
            GridSettings(1, 1, 12, 12, 16));
        AreEqual(1, layout.Capacity, "1×1 容量 1");
        AreEqual(1, layout.Count, "只放入 1 张图");
        AreEqual(132, layout.CanvasWidth, "画布宽 2*16+100");
        AreEqual(132, layout.CanvasHeight, "画布高 2*16+100");
        AreEqual(16, layout.Items[0].X, "X=margin");
        AreEqual(16, layout.Items[0].Y, "Y=margin");
        AreEqual(100, layout.Items[0].Width, "宽");
        AreEqual(100, layout.Items[0].Height, "高");
        AssertGridInfo(layout, 1, 1, 100, 100, 12, 12, 16, 1);
    }

    private static void LayoutGridCapacityTruncation()
    {
        CollageLayout layout = LayoutEngine.Compute(RepeatedSizes(5, 100, 100),
            GridSettings(2, 2, 12, 12, 16));
        AreEqual(4, layout.Capacity, "2×2 容量 4");
        AreEqual(4, layout.Count, "图片数>容量时只产出 4 个 LayoutItem");
        for (int i = 0; i < layout.Count; i++)
        {
            AreEqual(i, layout.Items[i].Index, "Index=图片下标");
        }
        AreEqual(128, layout.Items[3].X, "第 4 个区域 X");
        AreEqual(128, layout.Items[3].Y, "第 4 个区域 Y");
        AreEqual(244, layout.CanvasWidth, "画布宽 2*16+2*100+12");
    }

    private static void LayoutGridMax20x20()
    {
        CollageLayout layout = LayoutEngine.Compute(RepeatedSizes(210, 100, 100),
            GridSettings(20, 20, 12, 12, 16));
        AreEqual(400, layout.Capacity, "20×20 容量 400");
        AreEqual(210, layout.Count, "210 张图全部放入");
        AreEqual(2260, layout.CanvasWidth, "画布宽 2*16+20*100+19*12");
        AreEqual(2260, layout.CanvasHeight, "画布高 2*16+20*100+19*12");
        AreEqual(1024, layout.Items[209].X, "最后一张 X=16+9*112");
        AreEqual(1136, layout.Items[209].Y, "最后一张 Y=16+10*112");
        AssertTrue(!layout.WasClamped, "20×20 小单元格不应 clamp");
    }

    private static void LayoutGridGapZeroSeamless()
    {
        CollageLayout layout = LayoutEngine.Compute(RepeatedSizes(4, 100, 100),
            GridSettings(2, 2, 0, 0, 16));
        AreEqual(232, layout.CanvasWidth, "画布宽 2*16+2*100（无间距）");
        AreEqual(232, layout.CanvasHeight, "画布高 2*16+2*100（无间距）");
        AreEqual(16, layout.Items[0].X, "第 1 格 X");
        AreEqual(116, layout.Items[1].X, "第 2 格 X 与第 1 格严格相接");
        AreEqual(116, layout.Items[2].Y, "第 3 格 Y 与第 1 格严格相接");
        AreEqual(layout.Items[0].X + layout.Items[0].Width, layout.Items[1].X,
            "第 1/2 格水平边界严格相接");
        AreEqual(layout.Items[2].X + layout.Items[2].Width, layout.Items[3].X,
            "第 3/4 格水平边界严格相接");
        AreEqual(layout.Items[0].Y + layout.Items[0].Height, layout.Items[2].Y,
            "第 1/3 格垂直边界严格相接");
        AreEqual(layout.Items[1].Y + layout.Items[1].Height, layout.Items[3].Y,
            "第 2/4 格垂直边界严格相接");
        AreEqual(0, layout.RowGap, "RowGap=0");
        AreEqual(0, layout.ColumnGap, "ColumnGap=0");
    }

    private static void LayoutGridWidthModeExact()
    {
        // 自然 356×356，目标宽 712（×2 精确）：主尺寸精确、网格元数据同步缩放
        CollageSettings settings = GridSettings(3, 3, 12, 12, 16);
        settings.OutputSizeMode = OutputSizeMode.Width;
        settings.OutputTarget = 712;
        CollageLayout layout = LayoutEngine.Compute(RepeatedSizes(9, 100, 100), settings);
        AreEqual(712, layout.CanvasWidth, "指定宽度精确等于目标");
        AreEqual(712, layout.CanvasHeight, "高度按比例缩放");
        AssertGridInfo(layout, 3, 3, 200, 200, 24, 24, 32, 9);
        AssertTrue(!layout.WasClamped, "正常目标不应 clamp");
        AreEqual(256, layout.Items[1].X, "第 2 张 X=round(128*2)");
        AreEqual(480, layout.Items[2].X, "第 3 张 X=round(240*2)");
        AreEqual(256, layout.Items[3].Y, "第 4 张 Y=round(128*2)");
        AreEqual(480, layout.Items[8].X, "第 9 张 X");
        AreEqual(480, layout.Items[8].Y, "第 9 张 Y");
        AreEqual(200, layout.Items[8].Width, "缩放后单元格宽");
        AreEqual(200, layout.Items[8].Height, "缩放后单元格高");
    }

    private static void LayoutGridHeightModeExact()
    {
        // 自然 356×356，目标高 178（×0.5 精确）
        CollageSettings settings = GridSettings(3, 3, 12, 12, 16);
        settings.OutputSizeMode = OutputSizeMode.Height;
        settings.OutputTarget = 178;
        CollageLayout layout = LayoutEngine.Compute(RepeatedSizes(9, 100, 100), settings);
        AreEqual(178, layout.CanvasHeight, "指定高度精确等于目标");
        AreEqual(178, layout.CanvasWidth, "宽度按比例缩放");
        AssertGridInfo(layout, 3, 3, 50, 50, 6, 6, 8, 9);
        AreEqual(64, layout.Items[1].X, "第 2 张 X=round(128*0.5)");
        AreEqual(120, layout.Items[2].X, "第 3 张 X=round(240*0.5)");
        AreEqual(64, layout.Items[3].Y, "第 4 张 Y");
        AreEqual(120, layout.Items[8].X, "第 9 张 X");
        AreEqual(120, layout.Items[8].Y, "第 9 张 Y");
        AreEqual(50, layout.Items[8].Width, "缩放后单元格宽");
        AreEqual(50, layout.Items[8].Height, "缩放后单元格高");
        AssertTrue(!layout.WasClamped, "正常目标不应 clamp");
    }

    private static void LayoutGridMergeWidthMode()
    {
        // 含合并区域 + 指定宽度：大格矩形与容量/元数据一起缩放
        CollageSettings settings = GridSettings(3, 3, 12, 12, 16);
        settings.Merges.Add(new GridRegion(0, 0, 2, 2));
        settings.OutputSizeMode = OutputSizeMode.Width;
        settings.OutputTarget = 712;
        CollageLayout layout = LayoutEngine.Compute(RepeatedSizes(7, 100, 100), settings);
        AreEqual(712, layout.CanvasWidth, "指定宽度精确等于目标");
        AreEqual(712, layout.CanvasHeight, "高度按比例缩放");
        AssertGridInfo(layout, 3, 3, 200, 200, 24, 24, 32, 6);
        AreEqual(6, layout.Count, "缩放后仍只放入 6 张图");
        AreEqual(424, layout.Items[0].Width, "大格宽 2*200+24（含跨越的内部列间距）");
        AreEqual(424, layout.Items[0].Height, "大格高 2*200+24");
        AreEqual(32, layout.Items[0].X, "大格 X=margin*2");
        AreEqual(480, layout.Items[1].X, "区域 2 X=round(240*2)");
        AreEqual(256, layout.Items[2].Y, "区域 3 Y=round(128*2)");
    }

    private static void LayoutGridSizeClamp()
    {
        List<ImageSize> sizes = new List<ImageSize>();
        sizes.Add(new ImageSize(50000, 50000));
        CollageLayout layout = LayoutEngine.Compute(sizes, GridSettings(3, 3, 12, 12, 16));
        AssertTrue(layout.WasClamped, "超大网格应触发尺寸保护");
        AssertTrue(layout.CanvasWidth <= LayoutEngine.DefaultMaxSide,
            "画布宽 <= 32000，实际 " + layout.CanvasWidth);
        AssertTrue(layout.CanvasHeight <= LayoutEngine.DefaultMaxSide,
            "画布高 <= 32000，实际 " + layout.CanvasHeight);
        AssertTrue((long)layout.CanvasWidth * layout.CanvasHeight <= LayoutEngine.DefaultMaxPixels,
            "像素 <= 8000 万，实际 " + ((long)layout.CanvasWidth * layout.CanvasHeight));
        AssertTrue(layout.CellWidth < 50000, "单元格被缩小，实际 " + layout.CellWidth);
        AreEqual(layout.CellWidth, layout.CellHeight, "正方形图片单元格宽高一致");
        AreEqual(2 * layout.GridMargin + 3 * layout.CellWidth + 2 * layout.ColumnGap,
            layout.CanvasWidth, "画布宽符合网格公式");
        AreEqual(2 * layout.GridMargin + 3 * layout.CellHeight + 2 * layout.RowGap,
            layout.CanvasHeight, "画布高符合网格公式");
        AreEqual(16, layout.GridMargin, "外边距不参与缩小");
        AreEqual(12, layout.ColumnGap, "列间距不参与缩小");
        AreEqual(12, layout.RowGap, "行间距不参与缩小");
        AreEqual(1, layout.Count, "单图仍只有 1 项");
        AreEqual(16, layout.Items[0].X, "X=margin（边距不缩）");
    }

    private static void LayoutGridInvalidMergesIgnored()
    {
        CollageSettings settings = GridSettings(3, 3, 12, 12, 16);
        settings.Merges.Add(null);                            // 空项
        settings.Merges.Add(new GridRegion(1, 1, 1, 1));      // 单格，忽略
        settings.Merges.Add(new GridRegion(2, 2, 2, 2));      // 越界，忽略
        settings.Merges.Add(new GridRegion(-1, 0, 1, 2));     // 负坐标，忽略
        settings.Merges.Add(new GridRegion(0, 0, 2, 2));      // 合法 2×2
        settings.Merges.Add(new GridRegion(1, 1, 2, 1));      // 部分重叠，忽略
        settings.Merges.Add(new GridRegion(0, 0, 2, 2));      // 完全重复，忽略
        CollageLayout layout = LayoutEngine.Compute(RepeatedSizes(7, 100, 100), settings);
        AreEqual(6, layout.Capacity, "非法合并被忽略后容量 9-3=6");
        AreEqual(6, layout.Count, "placed=min(7,6)=6");
        AreEqual(212, layout.Items[0].Width, "合法 2×2 大格宽 2*100+12");
        AreEqual(212, layout.Items[0].Height, "合法 2×2 大格高");
        AreEqual(240, layout.Items[1].X, "第 2 个区域 X=16+2*112");
        AreEqual(128, layout.Items[2].Y, "第 3 个区域 Y=16+112");
    }

    private static void LayoutGridClampInvalidSettings()
    {
        CollageSettings settings = GridSettings(99, -5, 999, -1, -3);
        CollageLayout layout = LayoutEngine.Compute(RepeatedSizes(2, 100, 100), settings);
        AreEqual(20, layout.GridRows, "行数夹到 20");
        AreEqual(1, layout.GridColumns, "列数夹到 1");
        AreEqual(200, layout.RowGap, "行间距夹到 200");
        AreEqual(0, layout.ColumnGap, "列间距夹到 0");
        AreEqual(0, layout.GridMargin, "外边距夹到 0");
        AreEqual(20, layout.Capacity, "20×1 容量 20");
        AreEqual(2, layout.Count, "2 张图放入 2 个区域");
        AreEqual(100, layout.CanvasWidth, "画布宽 0+1*100+0");
        AreEqual(5800, layout.CanvasHeight, "画布高 20*100+19*200");
        AreEqual(0, layout.Items[0].X, "X=margin=0");
        AreEqual(300, layout.Items[1].Y, "Y=0+1*(100+200)");
    }

    private static void LayoutGridEmptyAndSingle()
    {
        CollageLayout empty = LayoutEngine.Compute(new List<ImageSize>(),
            GridSettings(3, 3, 12, 12, 16));
        AreEqual(0, empty.CanvasWidth, "空网格画布宽 0");
        AreEqual(0, empty.CanvasHeight, "空网格画布高 0");
        AreEqual(0, empty.Count, "空网格项数 0");
        AssertTrue(!empty.WasClamped, "空网格不标记 clamp");

        // 网格模式不使用旧的自定义基准 BaseSize
        CollageSettings settings = GridSettings(3, 3, 12, 12, 16);
        settings.AutoBaseSize = false;
        settings.BaseSize = 50;
        CollageLayout single = LayoutEngine.Compute(RepeatedSizes(1, 800, 600), settings);
        AssertGridInfo(single, 3, 3, 800, 600, 12, 12, 16, 9);
        AreEqual(2456, single.CanvasWidth, "画布宽 2*16+3*800+2*12");
        AreEqual(1856, single.CanvasHeight, "画布高 2*16+3*600+2*12");
        AreEqual(1, single.Count, "单图只放入 1 项");
        AreEqual(0, single.Items[0].Index, "Index=0");
        AreEqual(16, single.Items[0].X, "X=margin");
        AreEqual(16, single.Items[0].Y, "Y=margin");
        AreEqual(800, single.Items[0].Width, "区域宽=cellW");
        AreEqual(600, single.Items[0].Height, "区域高=cellH");
    }

    // ---------------------------------------------------------------- 加载

    private static void LoaderSupportedExtensions()
    {
        string[] extensions = ImageLoader.SupportedExtensions;
        AssertTrue(extensions.Length >= 13, "扩展名数量，实际 " + extensions.Length);
        string[] expected = new string[]
        {
            ".jpg", ".jpeg", ".jpe", ".png", ".bmp", ".gif", ".tif", ".tiff",
            ".webp", ".heic", ".heif", ".avif", ".jfif"
        };
        for (int i = 0; i < expected.Length; i++)
        {
            AssertTrue(Array.IndexOf(extensions, expected[i]) >= 0, "缺少扩展名 " + expected[i]);
        }
        for (int i = 0; i < extensions.Length; i++)
        {
            string value = extensions[i];
            AssertTrue(value.Length > 1 && value[0] == '.', "扩展名应带点且小写：" + value);
            AreEqual(value.ToLowerInvariant(), value, "扩展名必须小写：" + value);
        }
        AssertTrue(ImageLoader.IsSupportedExtension("a.PNG"), ".PNG 应支持（大小写不敏感）");
        AssertTrue(!ImageLoader.IsSupportedExtension(null), "null 不支持");
        AssertTrue(!ImageLoader.IsSupportedExtension("a.txt"), ".txt 不支持");
        AssertTrue(!ImageLoader.IsSupportedExtension(""), "空字符串不支持");
        AssertTrue(ImageLoader.IsSupportedFile(Fixture("red40x20.png")), "存在的图片文件应支持");
        AssertTrue(!ImageLoader.IsSupportedFile(Fixture("not_exists.png")), "不存在的文件不支持");

        string filter = ImageLoader.FileDialogFilter;
        AssertTrue(filter.StartsWith("图片文件|", StringComparison.Ordinal), "过滤器前缀");
        AssertTrue(filter.Contains("*.webp") && filter.Contains("*.heic") && filter.Contains("*.png"),
            "过滤器包含主要格式：" + filter);
    }

    private static void LoaderBasicEditing()
    {
        ImageLoadResult result = ImageLoader.LoadForEditing(Fixture("red40x20.png"), 64, 32);
        try
        {
            AssertTrue(result.Success, "应成功：" + result.ErrorMessage);
            AreEqual(40, result.PixelWidth, "宽");
            AreEqual(20, result.PixelHeight, "高");
            AreEqual("red40x20.png", result.FileName, "文件名");
            AssertTrue(result.Preview != null, "预览非空");
            AssertTrue(result.Thumbnail != null, "缩略图非空");
            AreEqual(40, result.Preview.Width, "预览宽");
            AreEqual(20, result.Preview.Height, "预览高");
            AreEqual(32, result.Thumbnail.Width, "缩略图宽");
            AreEqual(16, result.Thumbnail.Height, "缩略图高");
            AreEqual((int)PixelFormat.Format32bppArgb, (int)result.Preview.PixelFormat, "预览格式 32bppArgb");
            Color pixel = result.Preview.GetPixel(20, 10);
            ColorNear(Red, pixel, 1, "预览像素应为红色");
        }
        finally
        {
            result.Dispose();
            result.Dispose(); // 可重复调用
        }
    }

    private static void LoaderNeverThrows()
    {
        string badText = Path.Combine(NewTempDir(), "notes.txt");
        File.WriteAllText(badText, "hello");

        string[] paths = new string[]
        {
            null,
            string.Empty,
            "   ",
            Path.Combine(_tempRoot, "missing.png"),
            _tempRoot,
            Fixture("corrupted.png"),
            Fixture("fake.heic"),
            badText
        };
        for (int i = 0; i < paths.Length; i++)
        {
            ImageLoadResult result = null;
            try
            {
                result = ImageLoader.LoadForEditing(paths[i], 64, 32);
            }
            catch (Exception ex)
            {
                throw new TestFailureException("LoadForEditing 不应抛异常（输入 " + (paths[i] ?? "<null>")
                    + "），却抛出 " + ex.GetType().Name + ": " + ex.Message);
            }
            try
            {
                AssertTrue(result != null, "应返回结果对象");
                AssertTrue(!result.Success, "应失败：" + (paths[i] ?? "<null>"));
                AssertTrue(!string.IsNullOrEmpty(result.ErrorMessage), "失败必须有中文错误信息");
                AssertTrue(result.Preview == null, "失败时预览应为 null");
                AssertTrue(result.Thumbnail == null, "失败时缩略图应为 null");
                AreEqual(0, result.PixelWidth, "失败时宽为 0");
                AreEqual(0, result.PixelHeight, "失败时高为 0");
            }
            finally
            {
                if (result != null)
                {
                    result.Dispose();
                }
            }
        }

        ImageLoadResult missing = ImageLoader.LoadForEditing(Path.Combine(_tempRoot, "missing.png"), 64, 32);
        try
        {
            AssertTrue(missing.ErrorMessage.Contains("不存在"), "缺失文件提示应含“不存在”，实际：" + missing.ErrorMessage);
        }
        finally
        {
            missing.Dispose();
        }

        ImageLoadResult directory = ImageLoader.LoadForEditing(_tempRoot, 64, 32);
        try
        {
            AssertTrue(directory.ErrorMessage.Contains("文件夹"), "目录提示应含“文件夹”，实际：" + directory.ErrorMessage);
        }
        finally
        {
            directory.Dispose();
        }
    }

    private static void LoaderFakeHeicMessage()
    {
        ImageLoadResult result = ImageLoader.LoadForEditing(Fixture("fake.heic"), 64, 32);
        try
        {
            AssertTrue(!result.Success, "假 HEIC 应失败");
            bool mentions = result.ErrorMessage != null
                && (result.ErrorMessage.Contains("HEIC") || result.ErrorMessage.Contains("HEIF"));
            AssertTrue(mentions, "错误信息应提示 HEIC/HEIF 解码器，实际：" + result.ErrorMessage);
        }
        finally
        {
            result.Dispose();
        }
    }

    private static void LoaderNoFileLock()
    {
        string directory = NewTempDir();
        string copy = Path.Combine(directory, "lock_test.png");
        File.Copy(Fixture("red40x20.png"), copy);

        ImageLoadResult result = ImageLoader.LoadForEditing(copy, 64, 32);
        try
        {
            AssertTrue(result.Success, "复制文件应加载成功：" + result.ErrorMessage);
            // 加载完成后源文件不应被占用：可独占打开、可删除
            using (FileStream stream = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                AssertTrue(stream.Length > 0, "可独占打开源文件");
            }
            File.Delete(copy);
            AssertTrue(!File.Exists(copy), "源文件应可删除（未被锁定）");
            AssertTrue(result.Preview != null, "删除源文件后预览仍可用");
        }
        finally
        {
            result.Dispose();
        }
    }

    private static void LoaderGifFirstFrame()
    {
        using (Bitmap full = ImageLoader.LoadFull(Fixture("anim.gif")))
        {
            AreEqual(32, full.Width, "GIF 首帧宽");
            AreEqual(24, full.Height, "GIF 首帧高");
            Color center = full.GetPixel(16, 12);
            ColorNearRgb(Red, center, 40, "GIF 首帧应为红色");
        }

        ImageLoadResult editing = ImageLoader.LoadForEditing(Fixture("anim.gif"), 64, 32);
        try
        {
            AssertTrue(editing.Success, "GIF 编辑加载应成功：" + editing.ErrorMessage);
            AreEqual(32, editing.PixelWidth, "GIF 编辑宽");
            AreEqual(24, editing.PixelHeight, "GIF 编辑高");
        }
        finally
        {
            editing.Dispose();
        }
    }

    private static void LoaderPreviewThumbSizesAndAlpha()
    {
        ImageLoadResult result = ImageLoader.LoadForEditing(Fixture("big200x100.png"), 50, 32);
        try
        {
            AssertTrue(result.Success, "应成功：" + result.ErrorMessage);
            AreEqual(200, result.PixelWidth, "原图宽");
            AreEqual(100, result.PixelHeight, "原图高");
            AssertTrue(result.Preview != null, "预览非空");
            AreEqual(50, result.Preview.Width, "预览长边=50");
            AreEqual(25, result.Preview.Height, "预览高=25");
            AssertTrue(result.Thumbnail != null, "缩略图非空");
            AreEqual(32, result.Thumbnail.Width, "缩略图长边=32");
            AreEqual(16, result.Thumbnail.Height, "缩略图高=16");

            Color left = result.Preview.GetPixel(10, 10);
            ColorNear(Red, left, 2, "预览左半红色不透明");
            Color right = result.Preview.GetPixel(40, 10);
            ColorNear(Color.FromArgb(128, 0, 255, 0), right, 3, "预览右半保留自身 alpha=128");
        }
        finally
        {
            result.Dispose();
        }
    }

    private static void LoaderExifOrientations()
    {
        // 行顺序：TL、TR、BL、BR；0=红 1=绿 2=蓝 3=黄（存储像素为四色块，EXIF 标记方向）
        int[][] expected = new int[9][];
        expected[1] = new int[] { 0, 1, 2, 3 };
        expected[2] = new int[] { 1, 0, 3, 2 };
        expected[3] = new int[] { 3, 2, 1, 0 };
        expected[4] = new int[] { 2, 3, 0, 1 };
        expected[5] = new int[] { 0, 2, 1, 3 };
        expected[6] = new int[] { 2, 0, 3, 1 };
        expected[7] = new int[] { 3, 1, 2, 0 };
        expected[8] = new int[] { 1, 3, 0, 2 };
        Color[] colors = new Color[] { Red, Green, Blue, Yellow };

        for (int orientation = 1; orientation <= 8; orientation++)
        {
            using (Bitmap bitmap = ImageLoader.LoadFull(Fixture("exif_o" + orientation + ".jpg")))
            {
                bool swapped = orientation >= 5;
                int expectedWidth = swapped ? 16 : 24;
                int expectedHeight = swapped ? 24 : 16;
                AreEqual(expectedWidth, bitmap.Width, "EXIF o" + orientation + " 宽");
                AreEqual(expectedHeight, bitmap.Height, "EXIF o" + orientation + " 高");

                int[] expectedColors = expected[orientation];
                Point[] centers = swapped
                    ? new Point[] { new Point(4, 6), new Point(12, 6), new Point(4, 18), new Point(12, 18) }
                    : new Point[] { new Point(6, 4), new Point(18, 4), new Point(6, 12), new Point(18, 12) };
                for (int block = 0; block < 4; block++)
                {
                    Color actual = bitmap.GetPixel(centers[block].X, centers[block].Y);
                    ColorNearRgb(colors[expectedColors[block]], actual, 45,
                        "EXIF o" + orientation + " 色块 " + block);
                    AreEqual(255, actual.A, "EXIF o" + orientation + " 不透明");
                }
            }
        }
    }

    private static void LoaderExifEditingDimensions()
    {
        for (int orientation = 1; orientation <= 8; orientation++)
        {
            ImageLoadResult result = ImageLoader.LoadForEditing(Fixture("exif_o" + orientation + ".jpg"), 64, 32);
            try
            {
                AssertTrue(result.Success, "EXIF o" + orientation + " 编辑加载失败：" + result.ErrorMessage);
                bool swapped = orientation >= 5;
                AreEqual(swapped ? 16 : 24, result.PixelWidth, "EXIF o" + orientation + " 编辑态宽（转正后）");
                AreEqual(swapped ? 24 : 16, result.PixelHeight, "EXIF o" + orientation + " 编辑态高（转正后）");
            }
            finally
            {
                result.Dispose();
            }
        }
    }

    private static void LoaderLoadFullExceptions()
    {
        AssertThrows<ImageLoadException>(delegate()
        {
            ImageLoader.LoadFull(Path.Combine(_tempRoot, "missing.png"));
        }, "缺失文件 LoadFull 应抛 ImageLoadException");

        AssertThrows<ImageLoadException>(delegate()
        {
            ImageLoader.LoadFull(Fixture("corrupted.png"));
        }, "损坏文件 LoadFull 应抛 ImageLoadException");

        AssertThrows<ImageLoadException>(delegate()
        {
            ImageLoader.LoadFull(string.Empty);
        }, "空路径 LoadFull 应抛 ImageLoadException");
    }

    private static void LoaderWebP()
    {
        ImageLoadResult result = ImageLoader.LoadForEditing(Fixture("pattern40x20.webp"), 64, 32);
        try
        {
            if (!result.Success)
            {
                if (result.ErrorMessage != null && result.ErrorMessage.Contains("WebP"))
                {
                    _skipped++;
                    Console.WriteLine("       [SKIP] 当前环境缺少 WebP 解码器：" + result.ErrorMessage);
                    return;
                }
                throw new TestFailureException("WebP 加载失败：" + result.ErrorMessage);
            }

            AreEqual(40, result.PixelWidth, "WebP 宽");
            AreEqual(20, result.PixelHeight, "WebP 高");
            Color left = result.Preview.GetPixel(10, 10);
            ColorNear(Red, left, 3, "WebP 左半红色");
            Color right = result.Preview.GetPixel(30, 10);
            ColorNear(Color.FromArgb(128, 0, 255, 0), right, 3, "WebP 右半保留 alpha");

            using (Bitmap exifBitmap = ImageLoader.LoadFull(Fixture("exif_o6.webp")))
            {
                AreEqual(16, exifBitmap.Width, "WebP EXIF o6 宽（转正后）");
                AreEqual(24, exifBitmap.Height, "WebP EXIF o6 高（转正后）");
                Color tl = exifBitmap.GetPixel(2, 2);
                ColorNearRgb(Blue, tl, 50, "WebP EXIF o6 左上应为蓝色");
            }
        }
        finally
        {
            result.Dispose();
        }
    }

    // ---------------------------------------------------------------- 合成/保存

    private static void ComposePreviewTransparentBackground()
    {
        List<ImageLoadResult> items = LoadItems("red40x20.png", "semi40x20.png");
        PreviewComposition composition = null;
        try
        {
            CollageSettings settings = new CollageSettings();
            composition = Compositor.ComposePreview(items, settings, 1000, 1000);
            AssertTrue(composition != null, "预览不应为 null");
            AreEqual(124, composition.Layout.CanvasWidth, "布局画布宽");
            AreEqual(52, composition.Layout.CanvasHeight, "布局画布高");
            AreEqual(124, composition.Bitmap.Width, "预览宽");
            AreEqual(52, composition.Bitmap.Height, "预览高");
            AssertTrue(Math.Abs(composition.Scale - 1.0f) < 0.001f, "Scale=1");

            Color gap = composition.Bitmap.GetPixel(62, 26);
            AreEqual(0, gap.A, "间隙背景应全透明");
            Color margin = composition.Bitmap.GetPixel(5, 5);
            AreEqual(0, margin.A, "外边距背景应全透明");
            Color first = composition.Bitmap.GetPixel(30, 26);
            ColorNear(Red, first, 2, "第一张图红色不透明");
            Color second = composition.Bitmap.GetPixel(88, 26);
            ColorNear(Color.FromArgb(128, 0, 255, 0), second, 3, "第二张图保留自身 alpha=128");
        }
        finally
        {
            if (composition != null)
            {
                composition.Dispose();
            }
            DisposeItems(items);
        }
    }

    private static void ComposePreviewOpaqueBackground()
    {
        List<ImageLoadResult> items = LoadItems("red40x20.png", "semi40x20.png");
        PreviewComposition composition = null;
        try
        {
            CollageSettings settings = new CollageSettings();
            settings.BackgroundColor = Color.FromArgb(255, 255, 0, 255); // 品红
            composition = Compositor.ComposePreview(items, settings, 1000, 1000);
            AssertTrue(composition != null, "预览不应为 null");
            Color gap = composition.Bitmap.GetPixel(62, 26);
            ColorNear(Color.FromArgb(255, 255, 0, 255), gap, 1, "间隙应为不透明品红");
            Color first = composition.Bitmap.GetPixel(30, 26);
            ColorNear(Red, first, 2, "图片本身颜色不受背景影响");
        }
        finally
        {
            if (composition != null)
            {
                composition.Dispose();
            }
            DisposeItems(items);
        }
    }

    private static void ComposePreviewSemiTransparentBackground()
    {
        List<ImageLoadResult> items = LoadItems("red40x20.png", "semi40x20.png");
        PreviewComposition composition = null;
        try
        {
            CollageSettings settings = new CollageSettings();
            settings.BackgroundColor = Color.FromArgb(128, 0, 0, 255);
            composition = Compositor.ComposePreview(items, settings, 1000, 1000);
            AssertTrue(composition != null, "预览不应为 null");
            Color gap = composition.Bitmap.GetPixel(62, 26);
            ColorNear(Color.FromArgb(128, 0, 0, 255), gap, 1, "半透明背景 alpha=128 应精确保留");
            Color first = composition.Bitmap.GetPixel(30, 26);
            ColorNear(Color.FromArgb(255, 255, 0, 0), first, 2, "不透明图片区域不受背景 alpha 影响");
        }
        finally
        {
            if (composition != null)
            {
                composition.Dispose();
            }
            DisposeItems(items);
        }
    }

    private static void ComposePreviewJpegFlattensWhite()
    {
        List<ImageLoadResult> items = LoadItems("red40x20.png", "semi40x20.png");
        PreviewComposition composition = null;
        try
        {
            CollageSettings settings = new CollageSettings();
            settings.Format = OutputFormat.Jpeg;
            composition = Compositor.ComposePreview(items, settings, 1000, 1000);
            AssertTrue(composition != null, "预览不应为 null");
            Color gap = composition.Bitmap.GetPixel(62, 26);
            ColorNear(Color.FromArgb(255, 255, 255, 255), gap, 1, "JPG 模式预览的透明区域应压成白色");
            int alpha = composition.Bitmap.GetPixel(5, 5).A;
            AreEqual(255, alpha, "JPG 模式预览必须不透明");
        }
        finally
        {
            if (composition != null)
            {
                composition.Dispose();
            }
            DisposeItems(items);
        }
    }

    private static void ComposePreviewScaleToMax()
    {
        List<ImageLoadResult> items = LoadItems("red40x20.png", "semi40x20.png");
        PreviewComposition composition = null;
        try
        {
            CollageSettings settings = new CollageSettings();
            composition = Compositor.ComposePreview(items, settings, 62, 26);
            AssertTrue(composition != null, "预览不应为 null");
            AreEqual(62, composition.Bitmap.Width, "预览宽按 maxWidth 缩放");
            AreEqual(26, composition.Bitmap.Height, "预览高按 maxHeight 缩放");
            AssertTrue(Math.Abs(composition.Scale - 0.5f) < 0.01f,
                "Scale 应为 0.5，实际 " + composition.Scale.ToString("0.###", CultureInfo.InvariantCulture));
            AreEqual(124, composition.Layout.CanvasWidth, "Layout 仍为最终像素尺寸");
            AreEqual(52, composition.Layout.CanvasHeight, "Layout 高仍为最终尺寸");
        }
        finally
        {
            if (composition != null)
            {
                composition.Dispose();
            }
            DisposeItems(items);
        }
    }

    private static void ComposePreviewEmptyNull()
    {
        CollageSettings settings = new CollageSettings();
        AssertTrue(Compositor.ComposePreview(null, settings, 100, 100) == null, "null 列表返回 null");
        AssertTrue(Compositor.ComposePreview(new List<ImageLoadResult>(), settings, 100, 100) == null,
            "空列表返回 null");

        ImageLoadResult failed = ImageLoader.LoadForEditing(Fixture("corrupted.png"), 64, 32);
        try
        {
            List<ImageLoadResult> onlyFailed = new List<ImageLoadResult>();
            onlyFailed.Add(failed);
            AssertTrue(Compositor.ComposePreview(onlyFailed, settings, 100, 100) == null,
                "只有失败项时返回 null");
        }
        finally
        {
            failed.Dispose();
        }
    }

    private static void ComposeComposePngPixels()
    {
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        paths.Add(Fixture("semi40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20, 40, 20);
        CollageSettings settings = new CollageSettings();
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);

        using (Bitmap composed = Compositor.Compose(paths, sizes, settings, layout, null))
        {
            AssertTrue(composed != null, "合成结果非空");
            AreEqual((int)PixelFormat.Format32bppArgb, (int)composed.PixelFormat, "PNG 合成格式 32bppArgb");
            AreEqual(124, composed.Width, "合成宽");
            AreEqual(52, composed.Height, "合成高");
            AreEqual(0, composed.GetPixel(62, 26).A, "透明背景间隙 alpha=0");
            ColorNear(Red, composed.GetPixel(30, 26), 2, "图片1红色");
            ColorNear(Color.FromArgb(128, 0, 255, 0), composed.GetPixel(88, 26), 3, "图片2保留 alpha");
        }
    }

    private static void ComposeHorizontalTwoImagesPixels()
    {
        // 回归测试：横向两张图必须左右排列，第二张不能画到画布外（坐标 bug）
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        paths.Add(Fixture("pattern40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20, 40, 20);
        CollageSettings settings = new CollageSettings();
        settings.Gap = 12;
        settings.Margin = 16;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);

        AreEqual(124, layout.CanvasWidth, "横向两张 40x20 画布宽 2*16+80+12");
        AreEqual(52, layout.CanvasHeight, "横向画布高 2*16+20");
        AreEqual(16, layout.Items[0].X, "第 1 张 X=margin");
        AreEqual(16, layout.Items[0].Y, "第 1 张 Y=margin");
        AreEqual(68, layout.Items[1].X, "第 2 张 X=16+40+12");
        AreEqual(16, layout.Items[1].Y, "第 2 张 Y 应与第 1 张相同（同一条水平带）");

        using (Bitmap composed = Compositor.Compose(paths, sizes, settings, layout, null))
        {
            int secondLeftX = layout.Items[1].X;
            int centerY = layout.Items[1].Y + layout.Items[1].Height / 2;
            AssertTrue(composed.GetPixel(30, 26).A == 255, "第 1 张图应在画布内");
            ColorNear(Red, composed.GetPixel(30, 26), 2, "第 1 张图红色");

            Color secondLeft = composed.GetPixel(secondLeftX + 5, centerY);
            ColorNear(Red, secondLeft, 2, "第 2 张图左半应被绘制在 (X2+5) 处");
            Color secondRight = composed.GetPixel(secondLeftX + 35, centerY);
            ColorNear(Color.FromArgb(128, 0, 255, 0), secondRight, 3, "第 2 张图右半保留 alpha=128");

            AreEqual(0, composed.GetPixel(62, 26).A, "两张图之间的间隙应透明");
            AreEqual(0, composed.GetPixel(30, 44).A, "图片下方外边距应透明（不能有图被画到下一行）");
        }
    }

    private static void ComposeVerticalTwoImagesPixels()
    {
        // 纵向两张图必须上下排列
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        paths.Add(Fixture("pattern40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20, 40, 20);
        CollageSettings settings = new CollageSettings();
        settings.Orientation = CollageOrientation.Vertical;
        settings.Gap = 12;
        settings.Margin = 16;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);

        AreEqual(72, layout.CanvasWidth, "纵向画布宽 2*16+40");
        AreEqual(84, layout.CanvasHeight, "纵向画布高 2*16+20+20+12");
        AreEqual(16, layout.Items[0].X, "第 1 张 X=margin");
        AreEqual(16, layout.Items[0].Y, "第 1 张 Y=margin");
        AreEqual(16, layout.Items[1].X, "第 2 张 X 应与第 1 张相同（同一条垂直带）");
        AreEqual(48, layout.Items[1].Y, "第 2 张 Y=16+20+12");

        using (Bitmap composed = Compositor.Compose(paths, sizes, settings, layout, null))
        {
            int secondTopY = layout.Items[1].Y;
            AssertTrue(composed.GetPixel(30, 26).A == 255, "第 1 张图应在画布内");
            ColorNear(Red, composed.GetPixel(30, 26), 2, "第 1 张图红色");

            Color secondLeft = composed.GetPixel(20, secondTopY + 10);
            ColorNear(Red, secondLeft, 2, "第 2 张图左半应被绘制在 (Y2+10) 处");
            Color secondRight = composed.GetPixel(48, secondTopY + 10);
            ColorNear(Color.FromArgb(128, 0, 255, 0), secondRight, 3, "第 2 张图右半保留 alpha=128");

            AreEqual(0, composed.GetPixel(30, 44).A, "两张图之间的间隙应透明");
            AreEqual(0, composed.GetPixel(5, 5).A, "四周外边距应透明");
        }
    }

    private static void ComposeCliCase88x24()
    {
        // Lead 集成复现用例：--layout h --gap 4 --margin 2 两张 40x20 => 画布 88x24，
        // 第 1 张 x=2，第 2 张 x=46，都要落在 y=2 这一条水平带上。
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        paths.Add(Fixture("semi40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20, 40, 20);
        CollageSettings settings = new CollageSettings();
        settings.Gap = 4;
        settings.Margin = 2;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);

        AreEqual(88, layout.CanvasWidth, "CLI 用例画布宽 2*2+40+40+4");
        AreEqual(24, layout.CanvasHeight, "CLI 用例画布高 2*2+20");
        AreEqual(2, layout.Items[0].X, "第 1 张 X=2");
        AreEqual(2, layout.Items[0].Y, "第 1 张 Y=2");
        AreEqual(46, layout.Items[1].X, "第 2 张 X=2+40+4=46");
        AreEqual(2, layout.Items[1].Y, "第 2 张 Y=2（同一水平带，不能跑到画布外）");

        using (Bitmap composed = Compositor.Compose(paths, sizes, settings, layout, null))
        {
            AreEqual(88, composed.Width, "合成画布宽");
            AreEqual(24, composed.Height, "合成画布高");
            Color first = composed.GetPixel(20, 12);
            ColorNear(Red, first, 2, "第 1 张图应绘制在 (20,12)");
            AreEqual(0, composed.GetPixel(43, 12).A, "间距 4px 在 x=42..45 应透明");
            Color second = composed.GetPixel(66, 12);
            ColorNear(Color.FromArgb(128, 0, 255, 0), second, 3, "第 2 张图应绘制在 (66,12) 且保留 alpha");
            AreEqual(0, composed.GetPixel(1, 12).A, "左边距应透明");
            AreEqual(0, composed.GetPixel(86, 12).A, "右边距应透明");
            AreEqual(0, composed.GetPixel(20, 23).A, "下边距应透明（不能有图错位到下一行）");
        }
    }

    private static void ComposeComposeJpegIs24bppWhite()
    {
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        paths.Add(Fixture("semi40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20, 40, 20);
        CollageSettings settings = new CollageSettings();
        settings.Format = OutputFormat.Jpeg;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);

        using (Bitmap composed = Compositor.Compose(paths, sizes, settings, layout, null))
        {
            AreEqual((int)PixelFormat.Format24bppRgb, (int)composed.PixelFormat, "JPG 合成格式 24bpp");
            AreEqual(124, composed.Width, "合成宽");
            AreEqual(124, composed.Width, "合成宽");
            AreEqual(52, composed.Height, "合成高");
            Color gap = composed.GetPixel(62, 26);
            AreEqual(255, gap.A, "24bpp 位图 alpha=255");
            ColorNearRgb(Color.White, gap, 1, "透明背景在 JPG 中应压成白色");
            ColorNearRgb(Red, composed.GetPixel(30, 26), 2, "红色图片区域");
            ColorNearRgb(Color.FromArgb(255, 127, 255, 127), composed.GetPixel(88, 26), 4,
                "半透明绿压在白色上≈(127,255,127)");
        }
    }

    private static void ComposeSavePngRoundtrip()
    {
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        paths.Add(Fixture("semi40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20, 40, 20);
        CollageSettings settings = new CollageSettings();
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        string output = Path.Combine(NewTempDir(), "transparent.png");

        Compositor.Save(paths, sizes, settings, layout, output, null);
        AssertTrue(File.Exists(output), "PNG 文件应生成");

        using (Bitmap reloaded = ImageLoader.LoadFull(output))
        {
            AreEqual(124, reloaded.Width, "重新加载宽");
            AreEqual(52, reloaded.Height, "重新加载高");
            AreEqual(0, reloaded.GetPixel(62, 26).A, "PNG 间隙 alpha 必须为 0");
            Color second = reloaded.GetPixel(88, 26);
            AreEqual(128, second.A, "PNG 半透明像素 alpha 必须保留，实际 " + second.A);
            ColorNear(Color.FromArgb(255, 0, 255, 0), Color.FromArgb(255, second.R, second.G, second.B), 3,
                "PNG 半透明像素颜色");
            ColorNear(Red, reloaded.GetPixel(30, 26), 2, "PNG 不透明像素");
        }
    }

    private static void ComposeSavePngSemiBgRoundtrip()
    {
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        paths.Add(Fixture("semi40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20, 40, 20);
        CollageSettings settings = new CollageSettings();
        settings.BackgroundColor = Color.FromArgb(128, 0, 0, 255);
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        string output = Path.Combine(NewTempDir(), "semi_bg.png");

        Compositor.Save(paths, sizes, settings, layout, output, null);
        using (Bitmap reloaded = ImageLoader.LoadFull(output))
        {
            Color gap = reloaded.GetPixel(62, 26);
            ColorNear(Color.FromArgb(128, 0, 0, 255), gap, 1, "PNG 半透明背景 alpha 应精确为 128");
        }
    }

    private static void ComposeSaveJpegWhite()
    {
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        paths.Add(Fixture("semi40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20, 40, 20);
        CollageSettings settings = new CollageSettings();
        settings.Format = OutputFormat.Jpeg;
        settings.JpegQuality = 92;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        string output = Path.Combine(NewTempDir(), "white.jpg");

        Compositor.Save(paths, sizes, settings, layout, output, null);
        AssertTrue(File.Exists(output), "JPG 文件应生成");
        long length = new FileInfo(output).Length;
        AssertTrue(length > 100, "JPG 文件有内容，实际 " + length + " 字节");

        using (Bitmap reloaded = ImageLoader.LoadFull(output))
        {
            AreEqual(124, reloaded.Width, "JPG 重新加载宽");
            AreEqual(52, reloaded.Height, "JPG 重新加载高");
            Color gap = reloaded.GetPixel(62, 26);
            AreEqual(255, gap.A, "JPG 无 alpha（加载后 alpha=255）");
            ColorNearRgb(Color.White, gap, 5, "JPG 透明区域填白");
            ColorNearRgb(Color.FromArgb(255, 127, 255, 127), reloaded.GetPixel(88, 26), 8,
                "JPG 半透明绿压白");
        }
    }

    private static void ComposeSaveJpegSemiBgFlatten()
    {
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        paths.Add(Fixture("semi40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20, 40, 20);
        CollageSettings settings = new CollageSettings();
        settings.Format = OutputFormat.Jpeg;
        settings.BackgroundColor = Color.FromArgb(128, 0, 0, 255);
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        string output = Path.Combine(NewTempDir(), "semi_bg.jpg");

        Compositor.Save(paths, sizes, settings, layout, output, null);
        using (Bitmap reloaded = ImageLoader.LoadFull(output))
        {
            // 背景 (0,0,255)@alpha128 压白：R=128 G=128 B=255
            ColorNearRgb(Color.FromArgb(255, 128, 128, 255), reloaded.GetPixel(62, 26), 5,
                "半透明蓝背景压白后≈(128,128,255)");
        }
    }

    private static void ComposeProgressReporting()
    {
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        paths.Add(Fixture("semi40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20, 40, 20);
        CollageSettings settings = new CollageSettings();
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        ClassProgress progress = new ClassProgress();
        string output = Path.Combine(NewTempDir(), "progress.png");

        Compositor.Save(paths, sizes, settings, layout, output, progress);
        AssertTrue(progress.Values.Count >= 3, "进度回调次数不足：" + progress.Values.Count);
        AreEqual(0, progress.Values[0], "进度应从 0 开始");
        AreEqual(100, progress.Values[progress.Values.Count - 1], "进度应以 100 结束");
        for (int i = 1; i < progress.Values.Count; i++)
        {
            AssertTrue(progress.Values[i] >= progress.Values[i - 1], "进度必须单调不减");
        }
    }

    private static void ComposeSaveDirectoryMissing()
    {
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20);
        CollageSettings settings = new CollageSettings();
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        string output = Path.Combine(_tempRoot, "no_such_directory", "x.png");
        try
        {
            Compositor.Save(paths, sizes, settings, layout, output, null);
            throw new TestFailureException("保存到不存在的目录应抛异常");
        }
        catch (TestFailureException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AssertTrue(ex.Message.Contains("目录不存在"), "提示应含“目录不存在”，实际：" + ex.Message);
        }
    }

    private static void ComposeArgumentValidation()
    {
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20);
        CollageSettings settings = new CollageSettings();
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);

        AssertThrows<ArgumentException>(delegate()
        {
            Compositor.Compose(paths, Sizes(40, 20, 40, 20), settings, layout, null);
        }, "数量不一致应抛 ArgumentException");

        AssertThrows<ArgumentException>(delegate()
        {
            Compositor.Compose(paths, sizes, settings, null, null);
        }, "layout 为 null 应抛 ArgumentException");

        AssertThrows<ArgumentException>(delegate()
        {
            Compositor.Save(paths, sizes, settings, layout, string.Empty, null);
        }, "空输出路径应抛 ArgumentException");
    }

    private static void ComposePreviewMatchesSavedPng()
    {
        List<ImageLoadResult> items = LoadItems("pattern40x20.png", "quadrants24x16.png");
        PreviewComposition composition = null;
        string output = Path.Combine(NewTempDir(), "preview_vs_save.png");
        List<string> paths = new List<string>();
        paths.Add(Fixture("pattern40x20.png"));
        paths.Add(Fixture("quadrants24x16.png"));
        try
        {
            List<ImageSize> sizes = new List<ImageSize>();
            for (int i = 0; i < items.Count; i++)
            {
                sizes.Add(new ImageSize(items[i].PixelWidth, items[i].PixelHeight));
            }
            CollageSettings settings = new CollageSettings();
            CollageLayout layout = LayoutEngine.Compute(sizes, settings);
            composition = Compositor.ComposePreview(items, settings, 4096, 4096);
            AssertTrue(composition != null, "预览非空");
            AssertTrue(composition.Bitmap.Width == layout.CanvasWidth
                && composition.Bitmap.Height == layout.CanvasHeight, "预览为 1:1 尺寸，便于像素比对");

            Compositor.Save(paths, sizes, settings, layout, output, null);
            using (Bitmap saved = ImageLoader.LoadFull(output))
            {
                AreEqual(layout.CanvasWidth, saved.Width, "保存图宽应与布局一致");
                AreEqual(layout.CanvasHeight, saved.Height, "保存图高应与布局一致");
                int mismatches = 0;
                string firstMismatch = null;
                for (int y = 0; y < layout.CanvasHeight; y += 3)
                {
                    for (int x = 0; x < layout.CanvasWidth; x += 3)
                    {
                        Color previewPixel = composition.Bitmap.GetPixel(x, y);
                        Color savedPixel = saved.GetPixel(x, y);
                        if (!ColorClose(previewPixel, savedPixel, 2))
                        {
                            mismatches++;
                            if (firstMismatch == null)
                            {
                                firstMismatch = "(" + x + "," + y + ") 预览 A" + previewPixel.A
                                    + "R" + previewPixel.R + "G" + previewPixel.G + "B" + previewPixel.B
                                    + " 保存 A" + savedPixel.A + "R" + savedPixel.R
                                    + "G" + savedPixel.G + "B" + savedPixel.B;
                            }
                        }
                    }
                }
                AssertTrue(mismatches == 0, "预览与保存结果像素应一致，差异点 " + mismatches + " 个，首个：" + firstMismatch);
            }
        }
        finally
        {
            if (composition != null)
            {
                composition.Dispose();
            }
            DisposeItems(items);
        }
    }

    // ------------------------------------------------ gap=0 无缝（第 2 轮迭代回归）

    private static void ComposeGapZeroSeamWidthMode()
    {
        // gap=0 + Width 模式放大 2.5 倍：相邻边界不得出现透明/背景色 1px 缝
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        paths.Add(Fixture("green40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20, 40, 20);
        CollageSettings settings = OutputModeSettings(OutputSizeMode.Width, 200);
        settings.Gap = 0;
        settings.Margin = 0;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(200, layout.CanvasWidth, "Width 模式画布宽");
        AreEqual(50, layout.CanvasHeight, "画布高 20*2.5");
        AreEqual(0, layout.Items[0].X, "第 1 张 X=0");
        AreEqual(100, layout.Items[0].Width, "第 1 张宽 100");
        AreEqual(100, layout.Items[1].X, "第 2 张 X=100（严格相接）");

        using (Bitmap composed = Compositor.Compose(paths, sizes, settings, layout, null))
        {
            AreEqual(200, composed.Width, "合成宽");
            AreEqual(50, composed.Height, "合成高");
            AreEqual(0, CountTranslucent(composed),
                "gap=0 全分辨率合成：任何像素 alpha 都必须为 255（半透明缝数=0）");
            AreEqual(0, CountNotNear(composed, Red, Green, 3),
                "gap=0 全分辨率合成：所有像素必须是红或绿（无背景色列）");
            ColorNearRgb(Red, composed.GetPixel(99, 25), 3, "边界左列 x=99 应为红");
            ColorNearRgb(Green, composed.GetPixel(100, 25), 3, "边界右列 x=100 应为绿");
            ColorNearRgb(Red, composed.GetPixel(0, 0), 3, "左上角红");
            ColorNearRgb(Green, composed.GetPixel(199, 49), 3, "右下角绿");
        }
    }

    private static void ComposeGapZeroSeamVertical()
    {
        // gap=0 + Height 模式放大 3 倍（纵向）
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        paths.Add(Fixture("green40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20, 40, 20);
        CollageSettings settings = OutputModeSettings(OutputSizeMode.Height, 120);
        settings.Orientation = CollageOrientation.Vertical;
        settings.Gap = 0;
        settings.Margin = 0;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(120, layout.CanvasHeight, "纵向 Height 模式画布高");
        AreEqual(120, layout.CanvasWidth, "纵向画布宽 40*3");
        AreEqual(60, layout.Items[0].Height, "第 1 张高 60");
        AreEqual(60, layout.Items[1].Y, "第 2 张 Y=60（严格相接）");

        using (Bitmap composed = Compositor.Compose(paths, sizes, settings, layout, null))
        {
            AreEqual(120, composed.Width, "合成宽");
            AreEqual(120, composed.Height, "合成高");
            AreEqual(0, CountTranslucent(composed), "gap=0 纵向合成：无半透明缝");
            AreEqual(0, CountNotNear(composed, Red, Green, 3), "gap=0 纵向合成：无背景色行");
            ColorNearRgb(Red, composed.GetPixel(60, 59), 3, "边界上行应为红");
            ColorNearRgb(Green, composed.GetPixel(60, 60), 3, "边界下行应为绿");
        }
    }

    private static void ComposeGapZeroPreviewSeam()
    {
        // 预览边界取整回归：margin=15 / base=12 / 预览 maxWidth=37，旧实现 item0 宽取整后会漏 1px 背景
        List<ImageLoadResult> items = LoadItems("red40x20.png", "green40x20.png");
        PreviewComposition composition = null;
        try
        {
            CollageSettings settings = new CollageSettings();
            settings.Gap = 0;
            settings.Margin = 15;
            settings.AutoBaseSize = false;
            settings.BaseSize = 12;
            List<ImageSize> sizes = new List<ImageSize>();
            sizes.Add(new ImageSize(items[0].PixelWidth, items[0].PixelHeight));
            sizes.Add(new ImageSize(items[1].PixelWidth, items[1].PixelHeight));
            CollageLayout layout = LayoutEngine.Compute(sizes, settings);
            AreEqual(78, layout.CanvasWidth, "布局宽 2*15+24+24");
            AreEqual(42, layout.CanvasHeight, "布局高 2*15+12");
            AreEqual(15, layout.Items[0].X, "布局 X1=15");
            AreEqual(39, layout.Items[1].X, "布局 X2=39（gap=0 严格相接）");

            composition = Compositor.ComposePreview(items, settings, 37, 1000);
            AssertTrue(composition != null, "预览非空");
            AreEqual(37, composition.Bitmap.Width, "预览宽 37");
            AreEqual(20, composition.Bitmap.Height, "预览高 20");
            // 边界取整后：item0=[7,19)，item1=[19,30)，图片带 y=[7,13)
            AreEqual(0, CountTranslucentInRect(composition.Bitmap, 7, 7, 23, 6),
                "预览 gap=0 图片带内 alpha 必须全 255（旧实现 x=18 会漏背景）");
            AreEqual(0, CountNotNearInRect(composition.Bitmap, 7, 7, 23, 6, Red, Green, 4),
                "预览 gap=0 图片带内不得出现背景色列");
            ColorNearRgb(Red, composition.Bitmap.GetPixel(18, 10), 4, "预览边界左列红");
            ColorNearRgb(Green, composition.Bitmap.GetPixel(19, 10), 4, "预览边界右列绿");
        }
        finally
        {
            if (composition != null)
            {
                composition.Dispose();
            }
            DisposeItems(items);
        }
    }

    private static void ComposeGapZeroPreviewFromBigPreviews()
    {
        // 大幅缩小：先验证 LoadForEditing 预览缩略图边缘不透明（TileFlipXY），再验证预览合成长边缩放后无透明缝
        List<ImageLoadResult> items = LoadItemsWithPreview(100, "big_red200x100.png", "big_green200x100.png");
        PreviewComposition composition = null;
        try
        {
            AreEqual(100, items[0].Preview.Width, "预览缩略图宽");
            AreEqual(50, items[0].Preview.Height, "预览缩略图高");
            AreEqual(255, items[0].Preview.GetPixel(0, 0).A, "预览缩略图左上角必须不透明（无缩放毛边）");
            AreEqual(255, items[0].Preview.GetPixel(99, 49).A, "预览缩略图右下角必须不透明");

            CollageSettings settings = OutputModeSettings(OutputSizeMode.Width, 100);
            settings.Gap = 0;
            settings.Margin = 0;
            List<ImageSize> sizes = Sizes(200, 100, 200, 100);
            CollageLayout layout = LayoutEngine.Compute(sizes, settings);
            AreEqual(100, layout.CanvasWidth, "宽 400 -> 100");
            AreEqual(25, layout.CanvasHeight, "高 100 -> 25");
            AreEqual(50, layout.Items[1].X, "第 2 张 X=50");

            composition = Compositor.ComposePreview(items, settings, 100, 100);
            AssertTrue(composition != null, "预览非空");
            AreEqual(100, composition.Bitmap.Width, "预览宽");
            AreEqual(25, composition.Bitmap.Height, "预览高");
            AreEqual(0, CountTranslucent(composition.Bitmap), "大幅缩小预览不得有半透明缝");
            AreEqual(0, CountNotNear(composition.Bitmap, Red, Green, 4), "大幅缩小预览无背景色列");
            ColorNearRgb(Red, composition.Bitmap.GetPixel(49, 12), 4, "边界左列红");
            ColorNearRgb(Green, composition.Bitmap.GetPixel(50, 12), 4, "边界右列绿");
        }
        finally
        {
            if (composition != null)
            {
                composition.Dispose();
            }
            DisposeItems(items);
        }
    }

    private static void ComposeGapZeroJpegNoBackgroundColumn()
    {
        // JPG（白底）gap=0：边界不得出现白色背景列
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        paths.Add(Fixture("green40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20, 40, 20);
        CollageSettings settings = OutputModeSettings(OutputSizeMode.Width, 200);
        settings.Gap = 0;
        settings.Margin = 0;
        settings.Format = OutputFormat.Jpeg;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        using (Bitmap composed = Compositor.Compose(paths, sizes, settings, layout, null))
        {
            AreEqual((int)PixelFormat.Format24bppRgb, (int)composed.PixelFormat, "JPG 合成为 24bpp");
            AreEqual(200, composed.Width, "合成宽");
            AreEqual(50, composed.Height, "合成高");
            AreEqual(0, CountNotNear(composed, Red, Green, 3), "JPG gap=0 不得出现白底列");
            AreEqual(255, composed.GetPixel(99, 25).A, "24bpp 位图 alpha=255");
            ColorNearRgb(Red, composed.GetPixel(99, 25), 3, "JPG 边界左列红");
            ColorNearRgb(Green, composed.GetPixel(100, 25), 3, "JPG 边界右列绿");
        }
    }

    // ------------------------------------------------ 网格合成（v1.1）

    private static void ComposeGridTransparentGaps()
    {
        List<string> paths = new List<string>();
        paths.Add(Fixture("big_red200x100.png"));
        paths.Add(Fixture("big_green200x100.png"));
        List<ImageSize> sizes = Sizes(200, 100, 200, 100);
        CollageSettings settings = GridSettings(2, 2, 12, 12, 16);
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(444, layout.CanvasWidth, "网格画布宽 2*16+2*200+12");
        AreEqual(244, layout.CanvasHeight, "网格画布高 2*16+2*100+12");
        AreEqual(2, layout.Count, "只放入 2 张图（其余格子留空）");

        using (Bitmap composed = Compositor.Compose(paths, sizes, settings, layout, null))
        {
            AreEqual(444, composed.Width, "合成宽");
            AreEqual(244, composed.Height, "合成高");
            ColorNear(Red, composed.GetPixel(116, 66), 2, "第 1 格图片红色");
            ColorNear(Green, composed.GetPixel(328, 66), 2, "第 2 格图片绿色");
            AreEqual(0, composed.GetPixel(222, 66).A, "列间距应透明");
            AreEqual(0, composed.GetPixel(100, 122).A, "行间距应透明");
            AreEqual(0, composed.GetPixel(5, 5).A, "外边距应透明");
            AreEqual(0, composed.GetPixel(116, 180).A, "空位（第 2 行）应为背景透明");
        }
    }

    private static void ComposeGridMergeContain()
    {
        List<string> paths = new List<string>();
        paths.Add(Fixture("big_red200x100.png"));
        paths.Add(Fixture("big_green200x100.png"));
        List<ImageSize> sizes = Sizes(200, 100, 200, 100);
        CollageSettings settings = GridSettings(2, 2, 12, 12, 16);
        settings.Merges.Add(new GridRegion(0, 0, 1, 2)); // 顶行 1×2 大格
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(3, layout.Capacity, "1×2 合并后容量 3");
        AreEqual(2, layout.Count, "2 张图放入 2 个区域");
        AreEqual(0, layout.Items[0].Index, "大格放第 1 张图");
        AreEqual(1, layout.Items[1].Index, "第 2 张图放入下一区域");
        AreEqual(412, layout.Items[0].Width, "大格宽 2*200+12");
        AreEqual(16, layout.Items[0].X, "大格 X");
        AreEqual(16, layout.Items[1].X, "第 2 个区域 X");
        AreEqual(128, layout.Items[1].Y, "第 2 个区域 Y");

        using (Bitmap composed = Compositor.Compose(paths, sizes, settings, layout, null))
        {
            // 200×100 的图在 412×100 大格内等比 contain 居中：左右各留 106px 背景
            AreEqual(0, composed.GetPixel(66, 66).A, "大格内图片左侧应为背景（透明）");
            AreEqual(0, composed.GetPixel(400, 66).A, "大格内图片右侧应为背景（透明）");
            ColorNear(Red, composed.GetPixel(222, 66), 2, "大格内图片应居中显示");
            ColorNear(Green, composed.GetPixel(116, 178), 2, "第 2 张图在下一行左侧");
        }
    }

    private static void ComposeGridEmptySlotsBackground()
    {
        List<string> paths = new List<string>();
        paths.Add(Fixture("red40x20.png"));
        List<ImageSize> sizes = Sizes(40, 20);
        CollageSettings settings = GridSettings(3, 3, 12, 12, 16);
        settings.BackgroundColor = Color.FromArgb(255, 0, 0, 255); // 不透明蓝
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);
        AreEqual(176, layout.CanvasWidth, "画布宽 2*16+3*40+2*12");
        AreEqual(116, layout.CanvasHeight, "画布高 2*16+3*20+2*12");
        AreEqual(1, layout.Count, "1 张图只占第 1 格");

        using (Bitmap composed = Compositor.Compose(paths, sizes, settings, layout, null))
        {
            ColorNear(Red, composed.GetPixel(36, 26), 2, "第 1 格为图片红色");
            ColorNear(Color.FromArgb(255, 0, 0, 255), composed.GetPixel(88, 26), 1,
                "空位（第 1 行第 2 列）显示背景色");
            ColorNear(Color.FromArgb(255, 0, 0, 255), composed.GetPixel(140, 90), 1,
                "空位（第 3 行第 3 列）显示背景色");
            ColorNear(Color.FromArgb(255, 0, 0, 255), composed.GetPixel(5, 5), 1, "外边距显示背景色");
            AreEqual(255, composed.GetPixel(88, 26).A, "不透明背景 alpha=255");
        }
    }

    private static void ComposeGridJpegWhiteBackground()
    {
        List<string> paths = new List<string>();
        paths.Add(Fixture("big_red200x100.png"));
        paths.Add(Fixture("big_green200x100.png"));
        List<ImageSize> sizes = Sizes(200, 100, 200, 100);
        CollageSettings settings = GridSettings(2, 2, 12, 12, 16);
        settings.Format = OutputFormat.Jpeg;
        CollageLayout layout = LayoutEngine.Compute(sizes, settings);

        using (Bitmap composed = Compositor.Compose(paths, sizes, settings, layout, null))
        {
            AreEqual((int)PixelFormat.Format24bppRgb, (int)composed.PixelFormat, "JPG 网格合成为 24bpp");
            ColorNearRgb(Color.White, composed.GetPixel(222, 66), 1, "列间距在白底 JPG 中为白色");
            ColorNearRgb(Color.White, composed.GetPixel(5, 5), 1, "外边距为白色");
            ColorNearRgb(Color.White, composed.GetPixel(116, 180), 1, "空位为白色");
            ColorNearRgb(Red, composed.GetPixel(116, 66), 2, "图片区域为红色");
            ColorNearRgb(Green, composed.GetPixel(328, 66), 2, "图片区域为绿色");
        }
    }

    private static void ComposeGridPreviewMatchesSave()
    {
        List<ImageLoadResult> items = LoadItems("big_red200x100.png", "big_green200x100.png");
        PreviewComposition composition = null;
        string output = Path.Combine(NewTempDir(), "grid_preview_vs_save.png");
        List<string> paths = new List<string>();
        paths.Add(Fixture("big_red200x100.png"));
        paths.Add(Fixture("big_green200x100.png"));
        try
        {
            List<ImageSize> sizes = new List<ImageSize>();
            for (int i = 0; i < items.Count; i++)
            {
                sizes.Add(new ImageSize(items[i].PixelWidth, items[i].PixelHeight));
            }
            CollageSettings settings = GridSettings(2, 2, 12, 12, 16);
            CollageLayout layout = LayoutEngine.Compute(sizes, settings);
            composition = Compositor.ComposePreview(items, settings, 4096, 4096);
            AssertTrue(composition != null, "网格预览非空");
            AssertTrue(composition.Layout.IsGrid, "预览应携带网格元数据");
            AreEqual(layout.CanvasWidth, composition.Bitmap.Width, "预览为 1:1 宽，便于像素比对");
            AreEqual(layout.CanvasHeight, composition.Bitmap.Height, "预览为 1:1 高");
            AssertTrue(Math.Abs(composition.Scale - 1.0f) < 0.001f, "Scale=1");

            Compositor.Save(paths, sizes, settings, layout, output, null);
            using (Bitmap saved = ImageLoader.LoadFull(output))
            {
                AreEqual(layout.CanvasWidth, saved.Width, "保存图宽与布局一致");
                AreEqual(layout.CanvasHeight, saved.Height, "保存图高与布局一致");
                int mismatches = 0;
                string firstMismatch = null;
                for (int y = 0; y < layout.CanvasHeight; y += 3)
                {
                    for (int x = 0; x < layout.CanvasWidth; x += 3)
                    {
                        Color previewPixel = composition.Bitmap.GetPixel(x, y);
                        Color savedPixel = saved.GetPixel(x, y);
                        if (!ColorClose(previewPixel, savedPixel, 2))
                        {
                            mismatches++;
                            if (firstMismatch == null)
                            {
                                firstMismatch = "(" + x + "," + y + ") 预览 A" + previewPixel.A
                                    + "R" + previewPixel.R + "G" + previewPixel.G + "B" + previewPixel.B
                                    + " 保存 A" + savedPixel.A + "R" + savedPixel.R
                                    + "G" + savedPixel.G + "B" + savedPixel.B;
                            }
                        }
                    }
                }
                AssertTrue(mismatches == 0, "网格预览与保存结果像素应一致，差异点 " + mismatches
                    + " 个，首个：" + firstMismatch);
            }

            // 缩小预览：Layout 仍为输出像素尺寸，Scale <= 1
            PreviewComposition scaled = Compositor.ComposePreview(items, settings, 222, 122);
            try
            {
                AssertTrue(scaled != null, "缩小预览非空");
                AreEqual(222, scaled.Bitmap.Width, "缩小预览宽");
                AreEqual(122, scaled.Bitmap.Height, "缩小预览高");
                AssertTrue(Math.Abs(scaled.Scale - 0.5f) < 0.01f,
                    "缩小预览 Scale≈0.5，实际 " + scaled.Scale.ToString("0.###", CultureInfo.InvariantCulture));
                AreEqual(layout.CanvasWidth, scaled.Layout.CanvasWidth, "Layout 仍为输出尺寸");
                AreEqual(layout.CanvasHeight, scaled.Layout.CanvasHeight, "Layout 高仍为输出尺寸");
            }
            finally
            {
                scaled.Dispose();
            }
        }
        finally
        {
            if (composition != null)
            {
                composition.Dispose();
            }
            DisposeItems(items);
        }
    }

    private static void ComposeGridCapacityOverflow()
    {
        string[] names = new string[]
        {
            "red40x20.png", "green40x20.png", "semi40x20.png", "pattern40x20.png", "big_red200x100.png"
        };
        List<ImageLoadResult> items = LoadItems(names);
        PreviewComposition composition = null;
        try
        {
            List<string> paths = new List<string>();
            List<ImageSize> sizes = new List<ImageSize>();
            for (int i = 0; i < names.Length; i++)
            {
                paths.Add(Fixture(names[i]));
                sizes.Add(new ImageSize(items[i].PixelWidth, items[i].PixelHeight));
            }
            CollageSettings settings = GridSettings(2, 2, 12, 12, 16);
            CollageLayout layout = LayoutEngine.Compute(sizes, settings);
            AreEqual(4, layout.Capacity, "2×2 容量 4");
            AreEqual(4, layout.Count, "5 张图只放入 4 张");
            AreEqual(124, layout.CanvasWidth, "画布宽 2*16+2*40+12");
            AreEqual(84, layout.CanvasHeight, "画布高 2*16+2*20+12");

            composition = Compositor.ComposePreview(items, settings, 4096, 4096);
            AssertTrue(composition != null, "容量溢出时预览非空");
            AreEqual(4, composition.Layout.Count, "预览布局只含已放入的 4 项");
            AreEqual(124, composition.Bitmap.Width, "预览宽");
            AreEqual(84, composition.Bitmap.Height, "预览高");

            using (Bitmap composed = Compositor.Compose(paths, sizes, settings, layout, null))
            {
                AreEqual(124, composed.Width, "合成宽");
                AreEqual(84, composed.Height, "合成高");
                for (int y = 0; y < composed.Height; y += 2)
                {
                    for (int x = 0; x < composed.Width; x += 2)
                    {
                        Color previewPixel = composition.Bitmap.GetPixel(x, y);
                        Color composedPixel = composed.GetPixel(x, y);
                        AssertTrue(ColorClose(previewPixel, composedPixel, 2),
                            "容量溢出时预览与保存一致 @(" + x + "," + y + ")");
                    }
                }

                // 未放入网格的第 5 张图不得参与保存：与“只传前 4 张”的结果逐像素一致
                List<string> firstFourPaths = new List<string>();
                List<ImageSize> firstFourSizes = new List<ImageSize>();
                for (int i = 0; i < 4; i++)
                {
                    firstFourPaths.Add(paths[i]);
                    firstFourSizes.Add(sizes[i]);
                }
                using (Bitmap firstFour = Compositor.Compose(firstFourPaths, firstFourSizes, settings, layout, null))
                {
                    AreEqual(0, CountDifferentPixels(composed, firstFour, 2),
                        "未放入网格的第 5 张图不应参与保存");
                }
            }
        }
        finally
        {
            if (composition != null)
            {
                composition.Dispose();
            }
            DisposeItems(items);
        }
    }

    // ---------------------------------------------------------------- 设置

    private static void SettingsDefaults()
    {
        string directory = Path.Combine(_tempRoot, "settings_defaults");
        WithSettingsDir(directory, delegate()
        {
            AppSettings settings = SettingsStore.Load();
            AreEqual((int)CollageOrientation.Horizontal, (int)settings.Collage.Orientation, "默认横向");
            AreEqual(12, settings.Collage.Gap, "默认间距 12");
            AreEqual(16, settings.Collage.Margin, "默认边距 16");
            AreEqual(0, settings.Collage.BackgroundArgb, "默认背景全透明");
            AssertTrue(settings.Collage.AutoBaseSize, "默认自动基准");
            AreEqual(0, settings.Collage.BaseSize, "自动时基准 0");
            AreEqual((int)OutputFormat.Png, (int)settings.Collage.Format, "默认 PNG");
            AreEqual(92, settings.Collage.JpegQuality, "默认 JPG 质量 92");
            AreEqual((int)OutputSizeMode.Auto, (int)settings.Collage.OutputSizeMode, "默认输出尺寸模式 Auto");
            AreEqual(2000, settings.Collage.OutputTarget, "默认输出目标 2000");
            AreEqual(1180, settings.WindowWidth, "默认窗宽 1180");
            AreEqual(780, settings.WindowHeight, "默认窗高 780");
            AreEqual(int.MinValue, settings.WindowX, "默认 X 居中");
            AreEqual(int.MinValue, settings.WindowY, "默认 Y 居中");
            AssertTrue(!settings.WindowMaximized, "默认不最大化");
            AssertTrue(!settings.AutoOpenAfterSave, "默认保存后不打开");
        });
    }

    private static void SettingsRoundTrip()
    {
        string directory = Path.Combine(_tempRoot, "settings_roundtrip");
        WithSettingsDir(directory, delegate()
        {
            AppSettings original = new AppSettings();
            original.Collage.Orientation = CollageOrientation.Vertical;
            original.Collage.Gap = 3;
            original.Collage.Margin = 200;
            original.Collage.BackgroundArgb = unchecked((int)0x80FF0000);
            original.Collage.AutoBaseSize = false;
            original.Collage.BaseSize = 1234;
            original.Collage.Format = OutputFormat.Jpeg;
            original.Collage.JpegQuality = 88;
            original.WindowWidth = 1000;
            original.WindowHeight = 700;
            original.WindowX = -120;
            original.WindowY = 40;
            original.WindowMaximized = true;
            original.AutoOpenAfterSave = true;

            SettingsStore.Save(original);
            AssertTrue(File.Exists(SettingsStore.SettingsPath), "设置文件应生成");
            string raw = File.ReadAllText(SettingsStore.SettingsPath, Encoding.UTF8);
            AssertTrue(raw.Contains("BackgroundArgb=#80FF0000"), "ARGB 应写成 #AARRGGBB，实际：" + raw);

            AppSettings loaded = SettingsStore.Load();
            AreEqual((int)CollageOrientation.Vertical, (int)loaded.Collage.Orientation, "方向");
            AreEqual(3, loaded.Collage.Gap, "间距");
            AreEqual(200, loaded.Collage.Margin, "边距");
            AreEqual(unchecked((int)0x80FF0000), loaded.Collage.BackgroundArgb, "背景 ARGB");
            AssertTrue(!loaded.Collage.AutoBaseSize, "自动基准开关");
            AreEqual(1234, loaded.Collage.BaseSize, "基准尺寸");
            AreEqual((int)OutputFormat.Jpeg, (int)loaded.Collage.Format, "格式");
            AreEqual(88, loaded.Collage.JpegQuality, "质量");
            AreEqual(1000, loaded.WindowWidth, "窗宽");
            AreEqual(700, loaded.WindowHeight, "窗高");
            AreEqual(-120, loaded.WindowX, "窗口 X");
            AreEqual(40, loaded.WindowY, "窗口 Y");
            AssertTrue(loaded.WindowMaximized, "最大化");
            AssertTrue(loaded.AutoOpenAfterSave, "保存后打开");
        });
    }

    private static void SettingsCorruptAndClamp()
    {
        string directory = Path.Combine(_tempRoot, "settings_corrupt");
        WithSettingsDir(directory, delegate()
        {
            Directory.CreateDirectory(directory);
            string path = SettingsStore.SettingsPath;
            File.WriteAllText(path,
                "[collage]\r\n"
                + "Orientation=Banana\r\n"
                + "Gap=abc\r\n"
                + "Margin=-99\r\n"
                + "BackgroundArgb=zzz\r\n"
                + "AutoBaseSize=maybe\r\n"
                + "BaseSize=9999999\r\n"
                + "Format=Banana\r\n"
                + "JpegQuality=0\r\n"
                + "[window]\r\n"
                + "Width=10\r\n"
                + "Height=999999\r\n"
                + "X=abc\r\n"
                + "Maximized=maybe\r\n"
                + "[general]\r\n"
                + "AutoOpenAfterSave=maybe\r\n",
                Encoding.UTF8);

            AppSettings loaded = SettingsStore.Load();
            AreEqual((int)CollageOrientation.Horizontal, (int)loaded.Collage.Orientation, "非法方向回默认");
            AreEqual(12, loaded.Collage.Gap, "非法 Gap 回默认 12");
            AreEqual(0, loaded.Collage.Margin, "负数 Margin 夹到 0");
            AreEqual(0, loaded.Collage.BackgroundArgb, "非法背景回默认透明");
            AssertTrue(loaded.Collage.AutoBaseSize, "非法布尔回默认");
            AreEqual(100000, loaded.Collage.BaseSize, "过大 BaseSize 夹到 100000");
            AreEqual((int)OutputFormat.Png, (int)loaded.Collage.Format, "非法格式回默认 PNG");
            AreEqual(1, loaded.Collage.JpegQuality, "质量 0 夹到 1");
            AreEqual(640, loaded.WindowWidth, "过小窗宽夹到 640");
            AreEqual(10000, loaded.WindowHeight, "过大窗高夹到 10000");
            AreEqual(int.MinValue, loaded.WindowX, "非法 X 回默认居中");
            AssertTrue(!loaded.WindowMaximized, "非法最大化回默认 false");
            AssertTrue(!loaded.AutoOpenAfterSave, "非法保存后打开回默认 false");

            // 纯二进制垃圾也不应抛异常
            byte[] garbage = new byte[512];
            Random random = new Random(7);
            random.NextBytes(garbage);
            File.WriteAllBytes(path, garbage);
            AppSettings garbageLoaded = SettingsStore.Load();
            AreEqual(12, garbageLoaded.Collage.Gap, "垃圾文件也应返回默认值");
            SettingsStore.Save(garbageLoaded);
            AppSettings afterOverwrite = SettingsStore.Load();
            AreEqual(12, afterOverwrite.Collage.Gap, "垃圾文件后可重新保存读取");
        });
    }

    private static void SettingsDefaultPaths()
    {
        string key = SettingsStore.SettingsDirectoryOverrideVariable;
        string previous = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, null);
        try
        {
            string directory = SettingsStore.SettingsDirectory;
            AssertTrue(Path.IsPathRooted(directory), "设置目录应为绝对路径：" + directory);
            AssertTrue(directory.EndsWith("LightCollage", StringComparison.OrdinalIgnoreCase),
                "设置目录应以 LightCollage 结尾：" + directory);
            string path = SettingsStore.SettingsPath;
            AssertTrue(path.EndsWith("settings.ini", StringComparison.OrdinalIgnoreCase),
                "设置文件应为 settings.ini：" + path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }

    private static void SettingsOutputSizeRoundTrip()
    {
        string directory = Path.Combine(_tempRoot, "settings_outputsize");
        WithSettingsDir(directory, delegate()
        {
            AppSettings original = new AppSettings();
            original.Collage.OutputSizeMode = OutputSizeMode.Height;
            original.Collage.OutputTarget = 1234;
            SettingsStore.Save(original);
            AssertTrue(File.Exists(SettingsStore.SettingsPath), "设置文件应生成");
            string raw = File.ReadAllText(SettingsStore.SettingsPath, Encoding.UTF8);
            AssertTrue(raw.Contains("OutputSizeMode=Height"), "INI 应写入 OutputSizeMode=Height，实际：" + raw);
            AssertTrue(raw.Contains("OutputTarget=1234"), "INI 应写入 OutputTarget=1234，实际：" + raw);

            AppSettings loaded = SettingsStore.Load();
            AreEqual((int)OutputSizeMode.Height, (int)loaded.Collage.OutputSizeMode, "输出尺寸模式往返");
            AreEqual(1234, loaded.Collage.OutputTarget, "输出目标往返");

            loaded.Collage.OutputSizeMode = OutputSizeMode.Width;
            loaded.Collage.OutputTarget = 32000;
            SettingsStore.Save(loaded);
            AppSettings again = SettingsStore.Load();
            AreEqual((int)OutputSizeMode.Width, (int)again.Collage.OutputSizeMode, "Width 往返");
            AreEqual(32000, again.Collage.OutputTarget, "目标上限 32000 往返");

            // 与旧字段同时保存不互相破坏
            AreEqual((int)CollageOrientation.Horizontal, (int)again.Collage.Orientation, "旧字段方向仍可读");
            AreEqual(12, again.Collage.Gap, "旧字段间距仍可读");
        });
    }

    private static void SettingsOldConfigDefaults()
    {
        // 旧版 settings.ini（没有 OutputSizeMode/OutputTarget）应默认 Auto/2000，且旧字段正常读取
        string directory = Path.Combine(_tempRoot, "settings_oldconfig");
        WithSettingsDir(directory, delegate()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(SettingsStore.SettingsPath,
                "; v1 旧配置\r\n"
                + "[collage]\r\n"
                + "Orientation=Vertical\r\n"
                + "Gap=7\r\n"
                + "Margin=11\r\n"
                + "BackgroundArgb=#80000000\r\n"
                + "AutoBaseSize=0\r\n"
                + "BaseSize=600\r\n"
                + "Format=Jpeg\r\n"
                + "JpegQuality=85\r\n"
                + "[window]\r\n"
                + "Width=900\r\n"
                + "Height=650\r\n"
                + "X=10\r\n"
                + "Y=20\r\n"
                + "Maximized=0\r\n"
                + "[general]\r\n"
                + "AutoOpenAfterSave=0\r\n",
                Encoding.UTF8);

            AppSettings loaded = SettingsStore.Load();
            AreEqual((int)OutputSizeMode.Auto, (int)loaded.Collage.OutputSizeMode, "旧配置缺失模式默认 Auto");
            AreEqual(2000, loaded.Collage.OutputTarget, "旧配置缺失目标默认 2000");
            AreEqual((int)CollageOrientation.Vertical, (int)loaded.Collage.Orientation, "旧字段方向");
            AreEqual(7, loaded.Collage.Gap, "旧字段间距");
            AreEqual(11, loaded.Collage.Margin, "旧字段边距");
            AreEqual(unchecked((int)0x80000000), loaded.Collage.BackgroundArgb, "旧字段背景");
            AssertTrue(!loaded.Collage.AutoBaseSize, "旧字段自动基准");
            AreEqual(600, loaded.Collage.BaseSize, "旧字段基准");
            AreEqual((int)OutputFormat.Jpeg, (int)loaded.Collage.Format, "旧字段格式");
            AreEqual(85, loaded.Collage.JpegQuality, "旧字段质量");

            // 保存后新字段落盘，再次读取也正常
            SettingsStore.Save(loaded);
            AppSettings again = SettingsStore.Load();
            AreEqual((int)OutputSizeMode.Auto, (int)again.Collage.OutputSizeMode, "升级后模式仍为 Auto");
            AreEqual(2000, again.Collage.OutputTarget, "升级后目标仍为 2000");
        });
    }

    private static void SettingsOutputSizeClamp()
    {
        string directory = Path.Combine(_tempRoot, "settings_outputclamp");
        WithSettingsDir(directory, delegate()
        {
            Directory.CreateDirectory(directory);
            string path = SettingsStore.SettingsPath;

            File.WriteAllText(path,
                "[collage]\r\nOutputSizeMode=Banana\r\nOutputTarget=999999\r\n", Encoding.UTF8);
            AppSettings loaded = SettingsStore.Load();
            AreEqual((int)OutputSizeMode.Auto, (int)loaded.Collage.OutputSizeMode, "非法模式回 Auto");
            AreEqual(32000, loaded.Collage.OutputTarget, "过大目标夹到 32000");

            File.WriteAllText(path,
                "[collage]\r\nOutputSizeMode=Width\r\nOutputTarget=0\r\n", Encoding.UTF8);
            AppSettings widthZero = SettingsStore.Load();
            AreEqual((int)OutputSizeMode.Width, (int)widthZero.Collage.OutputSizeMode, "Width 模式保留");
            AreEqual(1, widthZero.Collage.OutputTarget, "目标 0 夹到 1");

            File.WriteAllText(path,
                "[collage]\r\nOutputSizeMode=Height\r\n", Encoding.UTF8);
            AppSettings heightMissing = SettingsStore.Load();
            AreEqual((int)OutputSizeMode.Height, (int)heightMissing.Collage.OutputSizeMode, "Height 模式保留");
            AreEqual(2000, heightMissing.Collage.OutputTarget, "缺失目标默认 2000");

            File.WriteAllText(path,
                "[collage]\r\nOutputSizeMode=2\r\nOutputTarget=abc\r\n", Encoding.UTF8);
            AppSettings numericMode = SettingsStore.Load();
            AreEqual((int)OutputSizeMode.Height, (int)numericMode.Collage.OutputSizeMode, "数字模式 2=Height");
            AreEqual(2000, numericMode.Collage.OutputTarget, "非法目标默认 2000");
        });
    }

    private static void SettingsSaveFailureSilent()
    {
        SettingsStore.Save(null); // 不应抛异常

        string blocker = Path.Combine(NewTempDir(), "blocker.txt");
        File.WriteAllText(blocker, "x");
        // 把设置目录指向一个文件路径的子目录 => CreateDirectory 必然失败
        WithSettingsDir(Path.Combine(blocker, "sub"), delegate()
        {
            SettingsStore.Save(new AppSettings()); // 静默失败，不抛异常
            AppSettings loaded = SettingsStore.Load();
            AreEqual(12, loaded.Collage.Gap, "目录不可写时 Load 返回默认值");
            AreEqual(16, loaded.Collage.Margin, "目录不可写时 Load 返回默认边距");
        });
    }

    // ------------------------------------------------ 网格设置（v1.1）

    private static void SettingsGridDefaults()
    {
        string directory = Path.Combine(_tempRoot, "settings_grid_defaults");
        WithSettingsDir(directory, delegate()
        {
            AppSettings settings = SettingsStore.Load();
            AreEqual(3, settings.Collage.GridRows, "默认网格行数 3");
            AreEqual(3, settings.Collage.GridColumns, "默认网格列数 3");
            AreEqual(12, settings.Collage.RowGap, "默认行间距 12");
            AreEqual(12, settings.Collage.ColumnGap, "默认列间距 12");
            AssertTrue(settings.Collage.Merges != null && settings.Collage.Merges.Count == 0,
                "默认无合并（非 null 空列表）");
            AssertTrue(settings.Collage.ShowGridGuides, "默认显示网格辅助线");
        });
    }

    private static void SettingsGridRoundTrip()
    {
        string directory = Path.Combine(_tempRoot, "settings_grid_roundtrip");
        WithSettingsDir(directory, delegate()
        {
            AppSettings original = new AppSettings();
            original.Collage.Orientation = CollageOrientation.Grid;
            original.Collage.GridRows = 5;
            original.Collage.GridColumns = 4;
            original.Collage.RowGap = 30;
            original.Collage.ColumnGap = 40;
            original.Collage.ShowGridGuides = false;
            original.Collage.Merges.Add(new GridRegion(0, 0, 2, 2));
            original.Collage.Merges.Add(new GridRegion(2, 2, 1, 2));

            SettingsStore.Save(original);
            AssertTrue(File.Exists(SettingsStore.SettingsPath), "设置文件应生成");
            string raw = File.ReadAllText(SettingsStore.SettingsPath, Encoding.UTF8);
            AssertTrue(raw.Contains("Orientation=Grid"), "INI 应写入 Orientation=Grid，实际：" + raw);
            AssertTrue(raw.Contains("GridRows=5"), "INI 应写入 GridRows=5");
            AssertTrue(raw.Contains("GridColumns=4"), "INI 应写入 GridColumns=4");
            AssertTrue(raw.Contains("RowGap=30"), "INI 应写入 RowGap=30");
            AssertTrue(raw.Contains("ColumnGap=40"), "INI 应写入 ColumnGap=40");
            AssertTrue(raw.Contains("GridMerges=1,1,2,2;3,3,3,4"), "合并应写为 1 基闭区间，实际：" + raw);
            AssertTrue(raw.Contains("ShowGridGuides=0"), "INI 应写入 ShowGridGuides=0");

            AppSettings loaded = SettingsStore.Load();
            AreEqual((int)CollageOrientation.Grid, (int)loaded.Collage.Orientation, "方向往返");
            AreEqual(5, loaded.Collage.GridRows, "行数往返");
            AreEqual(4, loaded.Collage.GridColumns, "列数往返");
            AreEqual(30, loaded.Collage.RowGap, "行间距往返");
            AreEqual(40, loaded.Collage.ColumnGap, "列间距往返");
            AssertTrue(!loaded.Collage.ShowGridGuides, "辅助线开关往返");
            AreEqual(2, loaded.Collage.Merges.Count, "合并数量往返");
            AreEqual(0, loaded.Collage.Merges[0].Row, "合并 1 行");
            AreEqual(0, loaded.Collage.Merges[0].Col, "合并 1 列");
            AreEqual(2, loaded.Collage.Merges[0].RowSpan, "合并 1 行跨度");
            AreEqual(2, loaded.Collage.Merges[0].ColSpan, "合并 1 列跨度");
            AreEqual(2, loaded.Collage.Merges[1].Row, "合并 2 行");
            AreEqual(2, loaded.Collage.Merges[1].Col, "合并 2 列");
            AreEqual(1, loaded.Collage.Merges[1].RowSpan, "合并 2 行跨度");
            AreEqual(2, loaded.Collage.Merges[1].ColSpan, "合并 2 列跨度");
        });
    }

    private static void SettingsGridClampAndCorrupt()
    {
        string directory = Path.Combine(_tempRoot, "settings_grid_clamp");
        WithSettingsDir(directory, delegate()
        {
            Directory.CreateDirectory(directory);
            string path = SettingsStore.SettingsPath;

            File.WriteAllText(path,
                "[collage]\r\n"
                + "Orientation=G\r\n"
                + "GridRows=99\r\n"
                + "GridColumns=0\r\n"
                + "RowGap=-7\r\n"
                + "ColumnGap=999\r\n"
                + "ShowGridGuides=maybe\r\n"
                + "GridMerges=1,1,2,2\r\n",
                Encoding.UTF8);
            AppSettings loaded = SettingsStore.Load();
            AreEqual((int)CollageOrientation.Grid, (int)loaded.Collage.Orientation, "G 解析为 Grid");
            AreEqual(20, loaded.Collage.GridRows, "行数 99 夹到 20");
            AreEqual(1, loaded.Collage.GridColumns, "列数 0 夹到 1");
            AreEqual(0, loaded.Collage.RowGap, "负行间距夹到 0");
            AreEqual(200, loaded.Collage.ColumnGap, "过大列间距夹到 200");
            AssertTrue(loaded.Collage.ShowGridGuides, "非法布尔回默认 true");
            AreEqual(0, loaded.Collage.Merges.Count, "列数夹到 1 后 2 列合并越界被丢弃");

            File.WriteAllText(path,
                "[collage]\r\n"
                + "Orientation=2\r\n"
                + "GridRows=3\r\n"
                + "GridColumns=3\r\n"
                + "GridMerges=1,1,2,2;0,0,1,1;9,9,9,9;banana;3,2,3,3\r\n",
                Encoding.UTF8);
            AppSettings parsed = SettingsStore.Load();
            AreEqual((int)CollageOrientation.Grid, (int)parsed.Collage.Orientation, "数字 2 解析为 Grid");
            AreEqual(2, parsed.Collage.Merges.Count, "非法合并项被丢弃");
            AreEqual(0, parsed.Collage.Merges[0].Row, "第 1 个合并行");
            AreEqual(0, parsed.Collage.Merges[0].Col, "第 1 个合并列");
            AreEqual(2, parsed.Collage.Merges[0].RowSpan, "第 1 个合并行跨度");
            AreEqual(2, parsed.Collage.Merges[0].ColSpan, "第 1 个合并列跨度");
            AreEqual(2, parsed.Collage.Merges[1].Row, "第 2 个合并行");
            AreEqual(1, parsed.Collage.Merges[1].Col, "第 2 个合并列");
            AreEqual(1, parsed.Collage.Merges[1].RowSpan, "第 2 个合并行跨度");
            AreEqual(2, parsed.Collage.Merges[1].ColSpan, "第 2 个合并列跨度");

            // 损坏文件（含网格键）仍回退默认值，不抛异常
            File.WriteAllText(path, "[collage]\r\nGridRows=abc\r\nGridColumns=xyz\r\nGridMerges=;;,\r\n",
                Encoding.UTF8);
            AppSettings corrupt = SettingsStore.Load();
            AreEqual(3, corrupt.Collage.GridRows, "损坏行数回默认 3");
            AreEqual(3, corrupt.Collage.GridColumns, "损坏列数回默认 3");
            AreEqual(0, corrupt.Collage.Merges.Count, "损坏合并串返回空列表");
        });
    }

    private static void SettingsGridOldConfigDefaults()
    {
        string directory = Path.Combine(_tempRoot, "settings_grid_oldconfig");
        WithSettingsDir(directory, delegate()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(SettingsStore.SettingsPath,
                "; v1.0 旧配置（无网格键）\r\n"
                + "[collage]\r\n"
                + "Orientation=Horizontal\r\n"
                + "Gap=7\r\n"
                + "Margin=11\r\n"
                + "[window]\r\n"
                + "Width=900\r\n"
                + "Height=650\r\n",
                Encoding.UTF8);

            AppSettings loaded = SettingsStore.Load();
            AreEqual(3, loaded.Collage.GridRows, "旧配置缺失行数默认 3");
            AreEqual(3, loaded.Collage.GridColumns, "旧配置缺失列数默认 3");
            AreEqual(12, loaded.Collage.RowGap, "旧配置缺失行间距默认 12");
            AreEqual(12, loaded.Collage.ColumnGap, "旧配置缺失列间距默认 12");
            AssertTrue(loaded.Collage.Merges != null && loaded.Collage.Merges.Count == 0,
                "旧配置无合并（非 null 空列表）");
            AssertTrue(loaded.Collage.ShowGridGuides, "旧配置默认显示辅助线");
            AreEqual(7, loaded.Collage.Gap, "旧字段间距仍可读");
            AreEqual(11, loaded.Collage.Margin, "旧字段边距仍可读");

            // 保存升级后网格默认值落盘，再次读取正常
            SettingsStore.Save(loaded);
            AppSettings again = SettingsStore.Load();
            AreEqual(3, again.Collage.GridRows, "升级后行数默认 3");
            AreEqual(3, again.Collage.GridColumns, "升级后列数默认 3");
            AreEqual(0, again.Collage.Merges.Count, "升级后无合并");
        });
    }

    // ---------------------------------------------------------------- 工具方法

    /// <summary>统计两张同尺寸位图在采样点（步长 2）上颜色差异超过容差的像素数；尺寸不同返回 -1。</summary>
    private static int CountDifferentPixels(Bitmap first, Bitmap second, int tolerance)
    {
        if (first.Width != second.Width || first.Height != second.Height)
        {
            return -1;
        }
        int count = 0;
        for (int y = 0; y < first.Height; y += 2)
        {
            for (int x = 0; x < first.Width; x += 2)
            {
                if (!ColorClose(first.GetPixel(x, y), second.GetPixel(x, y), tolerance))
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static void Test(string name, Action action)
    {
        try
        {
            action();
            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
        catch (Exception ex)
        {
            _failed++;
            string detail = name + " -> " + ex.GetType().Name + ": " + ex.Message;
            FailureDetails.Add(detail);
            Console.WriteLine("[FAIL] " + detail);
            if (!(ex is TestFailureException))
            {
                Console.WriteLine(ex.StackTrace);
            }
        }
    }

    private static List<ImageSize> Sizes(params int[] values)
    {
        if (values.Length % 2 != 0)
        {
            throw new TestFailureException("Sizes 参数必须成对");
        }
        List<ImageSize> sizes = new List<ImageSize>();
        for (int i = 0; i < values.Length; i += 2)
        {
            sizes.Add(new ImageSize(values[i], values[i + 1]));
        }
        return sizes;
    }

    private static List<ImageLoadResult> LoadItems(params string[] names)
    {
        List<ImageLoadResult> items = new List<ImageLoadResult>();
        try
        {
            for (int i = 0; i < names.Length; i++)
            {
                ImageLoadResult result = ImageLoader.LoadForEditing(Fixture(names[i]), 4096, 256);
                if (!result.Success)
                {
                    throw new TestFailureException("测试图加载失败：" + names[i] + " -> " + result.ErrorMessage);
                }
                items.Add(result);
            }
            return items;
        }
        catch (Exception)
        {
            DisposeItems(items);
            throw;
        }
    }

    private static List<ImageLoadResult> LoadItemsWithPreview(int previewMaxSide, params string[] names)
    {
        List<ImageLoadResult> items = new List<ImageLoadResult>();
        try
        {
            for (int i = 0; i < names.Length; i++)
            {
                ImageLoadResult result = ImageLoader.LoadForEditing(Fixture(names[i]), previewMaxSide, 32);
                if (!result.Success)
                {
                    throw new TestFailureException("测试图加载失败：" + names[i] + " -> " + result.ErrorMessage);
                }
                items.Add(result);
            }
            return items;
        }
        catch (Exception)
        {
            DisposeItems(items);
            throw;
        }
    }

    private static void DisposeItems(IList<ImageLoadResult> items)
    {
        if (items == null)
        {
            return;
        }
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null)
            {
                items[i].Dispose();
            }
        }
        items.Clear();
    }

    private static string Fixture(string name)
    {
        return Path.Combine(_fixtures, name);
    }

    private static string NewTempDir()
    {
        _tempCounter++;
        string path = Path.Combine(_tempRoot, "case" + _tempCounter.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WithSettingsDir(string directory, Action action)
    {
        string key = SettingsStore.SettingsDirectoryOverrideVariable;
        string previous = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, directory);
        try
        {
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }

    private static bool NearRgb(Color actual, Color expected, int tolerance)
    {
        return Math.Abs(expected.R - actual.R) <= tolerance
            && Math.Abs(expected.G - actual.G) <= tolerance
            && Math.Abs(expected.B - actual.B) <= tolerance;
    }

    /// <summary>统计整张位图 alpha&lt;255 的像素数（gap=0 无缝断言用）。</summary>
    private static int CountTranslucent(Bitmap bitmap)
    {
        return CountTranslucentInRect(bitmap, 0, 0, bitmap.Width, bitmap.Height);
    }

    private static int CountTranslucentInRect(Bitmap bitmap, int x, int y, int width, int height)
    {
        int count = 0;
        int right = Math.Min(bitmap.Width, x + width);
        int bottom = Math.Min(bitmap.Height, y + height);
        for (int py = Math.Max(0, y); py < bottom; py++)
        {
            for (int px = Math.Max(0, x); px < right; px++)
            {
                if (bitmap.GetPixel(px, py).A < 255)
                {
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>统计既不是颜色 A 也不是颜色 B 的像素数（检查是否出现背景色列/行）。</summary>
    private static int CountNotNear(Bitmap bitmap, Color first, Color second, int tolerance)
    {
        return CountNotNearInRect(bitmap, 0, 0, bitmap.Width, bitmap.Height, first, second, tolerance);
    }

    private static int CountNotNearInRect(Bitmap bitmap, int x, int y, int width, int height,
                                          Color first, Color second, int tolerance)
    {
        int count = 0;
        int right = Math.Min(bitmap.Width, x + width);
        int bottom = Math.Min(bitmap.Height, y + height);
        for (int py = Math.Max(0, y); py < bottom; py++)
        {
            for (int px = Math.Max(0, x); px < right; px++)
            {
                Color pixel = bitmap.GetPixel(px, py);
                if (!NearRgb(pixel, first, tolerance) && !NearRgb(pixel, second, tolerance))
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new TestFailureException(message);
        }
    }

    private static void AreEqual(int expected, int actual, string message)
    {
        if (expected != actual)
        {
            throw new TestFailureException(message + "（期望 " + expected + "，实际 " + actual + "）");
        }
    }

    private static void AreEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new TestFailureException(message + "（期望 " + expected + "，实际 " + actual + "）");
        }
    }

    private static void ColorNear(Color expected, Color actual, int tolerance, string message)
    {
        if (!ColorClose(expected, actual, tolerance))
        {
            throw new TestFailureException(message + "（期望 A" + expected.A + "R" + expected.R + "G" + expected.G
                + "B" + expected.B + "，实际 A" + actual.A + "R" + actual.R + "G" + actual.G + "B" + actual.B
                + "，容差 " + tolerance + "）");
        }
    }

    private static void ColorNearRgb(Color expected, Color actual, int tolerance, string message)
    {
        if (Math.Abs(expected.R - actual.R) > tolerance
            || Math.Abs(expected.G - actual.G) > tolerance
            || Math.Abs(expected.B - actual.B) > tolerance)
        {
            throw new TestFailureException(message + "（期望 R" + expected.R + "G" + expected.G + "B" + expected.B
                + "，实际 R" + actual.R + "G" + actual.G + "B" + actual.B + "，容差 " + tolerance + "）");
        }
    }

    private static bool ColorClose(Color expected, Color actual, int tolerance)
    {
        return Math.Abs(expected.A - actual.A) <= tolerance
            && Math.Abs(expected.R - actual.R) <= tolerance
            && Math.Abs(expected.G - actual.G) <= tolerance
            && Math.Abs(expected.B - actual.B) <= tolerance;
    }

    private static void AssertThrows<T>(Action action, string message) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new TestFailureException(message + "（实际抛出 " + ex.GetType().Name + ": " + ex.Message + "）");
        }
        throw new TestFailureException(message + "（没有抛出异常）");
    }

    private sealed class TestFailureException : Exception
    {
        public TestFailureException(string message)
            : base(message)
        {
        }
    }

    private sealed class ClassProgress : IProgress<int>
    {
        public readonly List<int> Values = new List<int>();

        public void Report(int value)
        {
            Values.Add(value);
        }
    }
}
