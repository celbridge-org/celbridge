using System.Runtime.InteropServices;

using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.WebHost.Platform;

public static partial class MacOSWebViewInterop
{
    private const string DeviceRgbColorSpaceName = "NSDeviceRGBColorSpace";

    private const int BitsPerSample = 8;
    private const int SamplesPerPixel = 4;

    // Sends initWithBitmapDataPlanes:... The shared runtime has no declaration with this many arguments.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageInitBitmapRep(
        IntPtr receiver,
        IntPtr selector,
        IntPtr planes,
        nint pixelsWide,
        nint pixelsHigh,
        nint bitsPerSample,
        nint samplesPerPixel,
        [MarshalAs(UnmanagedType.I1)] bool hasAlpha,
        [MarshalAs(UnmanagedType.I1)] bool isPlanar,
        IntPtr colorSpaceName,
        nint bytesPerRow,
        nint bitsPerPixel);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern CGSize SendMessageReturnCGSize(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(LibObjC)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGSize
    {
        public double Width;
        public double Height;
    }

    /// <summary>
    /// Draws an image file into a square PNG, centered with the image's proportions unchanged. Returns null for a file
    /// AppKit cannot read. Any thread can call this, because the method creates every object it uses and drains its own
    /// autorelease pool.
    /// </summary>
    public static byte[]? RenderImageAsPng(byte[] imageFile, int pixelSize)
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            var image = CreateNSImage(imageFile);
            if (image == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return DrawAsPng(image, pixelSize);
            }
            finally
            {
                SendMessage(image, GetSelector("release"));
            }
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private static IntPtr CreateNSImage(byte[] imageFile)
    {
        var pinnedFile = GCHandle.Alloc(imageFile, GCHandleType.Pinned);
        try
        {
            var data = SendMessage(
                GetClass("NSData"),
                GetSelector("dataWithBytes:length:"),
                pinnedFile.AddrOfPinnedObject(),
                imageFile.Length);

            var allocated = SendMessage(GetClass("NSImage"), GetSelector("alloc"));

            return SendMessage(allocated, GetSelector("initWithData:"), data);
        }
        finally
        {
            pinnedFile.Free();
        }
    }

    private static byte[]? DrawAsPng(IntPtr image, int pixelSize)
    {
        var bitmapRep = SendMessageInitBitmapRep(
            SendMessage(GetClass("NSBitmapImageRep"), GetSelector("alloc")),
            GetSelector("initWithBitmapDataPlanes:pixelsWide:pixelsHigh:bitsPerSample:samplesPerPixel:hasAlpha:isPlanar:colorSpaceName:bytesPerRow:bitsPerPixel:"),
            IntPtr.Zero,
            pixelSize,
            pixelSize,
            BitsPerSample,
            SamplesPerPixel,
            hasAlpha: true,
            isPlanar: false,
            CreateNSString(DeviceRgbColorSpaceName),
            0,
            0);
        if (bitmapRep == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var graphicsContextClass = GetClass("NSGraphicsContext");
            var context = SendMessage(graphicsContextClass, GetSelector("graphicsContextWithBitmapImageRep:"), bitmapRep);
            if (context == IntPtr.Zero)
            {
                return null;
            }

            SendMessage(graphicsContextClass, GetSelector("saveGraphicsState"));
            try
            {
                SendMessage(graphicsContextClass, GetSelector("setCurrentContext:"), context);
                SendMessageVoidCGRect(image, GetSelector("drawInRect:"), FitInSquare(image, pixelSize));
            }
            finally
            {
                SendMessage(graphicsContextClass, GetSelector("restoreGraphicsState"));
            }

            var properties = SendMessage(GetClass("NSDictionary"), GetSelector("dictionary"));
            var png = SendMessage(bitmapRep, GetSelector("representationUsingType:properties:"), BitmapImageFileTypePng, properties);

            return CopyNSData(png);
        }
        finally
        {
            SendMessage(bitmapRep, GetSelector("release"));
        }
    }

    // Returns where the image goes in the square. An image that does not report a size fills the square.
    private static CGRect FitInSquare(IntPtr image, int pixelSize)
    {
        var size = SendMessageReturnCGSize(image, GetSelector("size"));

        var longestSide = Math.Max(size.Width, size.Height);
        if (size.Width <= 0
            || size.Height <= 0)
        {
            return new CGRect { Width = pixelSize, Height = pixelSize };
        }

        var width = pixelSize * size.Width / longestSide;
        var height = pixelSize * size.Height / longestSide;

        return new CGRect
        {
            X = (pixelSize - width) / 2,
            Y = (pixelSize - height) / 2,
            Width = width,
            Height = height
        };
    }
}
