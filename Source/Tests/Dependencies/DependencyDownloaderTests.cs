using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Celbridge.Dependencies;
using Celbridge.Dependencies.Services;
using Celbridge.FileSystem.Services;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.Dependencies;

/// <summary>
/// Covers the download of a dependency's archive into a real folder. The cases are a matching hash and a mismatched
/// hash of each algorithm, and each kind of archive. The cases also cover a failed extraction, a failed request, and
/// a folder from another copy of Celbridge. A download that fails leaves neither the final folder nor the temporary folder.
/// </summary>
[TestFixture]
public class DependencyDownloaderTests
{
    private const string ZipUrl = "https://example.com/releases/1.2.3/tool-windows.zip";
    private const string TarGzUrl = "https://example.com/releases/1.2.3/tool-macos.tar.gz";
    private const string TgzUrl = "https://example.com/tool-macos/-/tool-macos-1.2.3.tgz";

    private string _parentFolderPath = null!;
    private string _folderPath = null!;

    [SetUp]
    public void Setup()
    {
        _parentFolderPath = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(DependencyDownloaderTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_parentFolderPath);
        _folderPath = Path.Combine(_parentFolderPath, "1.2.3");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_parentFolderPath))
        {
            Directory.Delete(_parentFolderPath, recursive: true);
        }
    }

    [Test]
    public async Task Download_AZipWithAMatchingHash_ExtractsTheArchiveIntoTheFolder()
    {
        var archive = CreateZip("tool.exe", "tool-helper.exe");
        var downloader = CreateDownloader(ZipUrl, archive);

        var result = await downloader.DownloadAsync(new DependencyDownload(ZipUrl, HashOf(archive), _folderPath));

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        ListFolder(_folderPath).Should().BeEquivalentTo("tool.exe", "tool-helper.exe");
        ListFolder(_parentFolderPath).Should().Equal("1.2.3");
    }

    [Test]
    public async Task Download_ATarGzWithAMatchingHash_InstallsTheArchiveFolder()
    {
        var archive = CreateTarGz("tool-macos", "tool");
        var downloader = CreateDownloader(TarGzUrl, archive);
        var download = new DependencyDownload(TarGzUrl, HashOf(archive), _folderPath)
        {
            ArchiveFolder = "tool-macos"
        };

        var result = await downloader.DownloadAsync(download);

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        ListFolder(_folderPath).Should().Equal("tool");
        ListFolder(_parentFolderPath).Should().Equal("1.2.3");
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(Path.Combine(_folderPath, "tool"));
            mode.Should().HaveFlag(UnixFileMode.UserExecute);
        }
    }

    [Test]
    public async Task Download_ATgzWithAMatchingSha512Hash_InstallsTheArchiveFolder()
    {
        var archive = CreateTarGz("package", "tool");
        var downloader = CreateDownloader(TgzUrl, archive);
        var download = new DependencyDownload(TgzUrl, Sha512HashOf(archive), _folderPath)
        {
            ArchiveFolder = "package"
        };

        var result = await downloader.DownloadAsync(download);

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        ListFolder(_folderPath).Should().Equal("tool");
        ListFolder(_parentFolderPath).Should().Equal("1.2.3");
    }

    [Test]
    public async Task Download_AMismatchedHash_FailsAndLeavesNoFolder()
    {
        var archive = CreateZip("tool.exe");
        var downloader = CreateDownloader(ZipUrl, archive);
        var wrongHash = new string('0', 64);
        var download = new DependencyDownload(ZipUrl, new DependencyHash(DependencyHashAlgorithm.Sha256, wrongHash), _folderPath);

        var result = await downloader.DownloadAsync(download);

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain(wrongHash);
        ListFolder(_parentFolderPath).Should().BeEmpty();
    }

    [Test]
    public async Task Download_AMismatchedSha512Hash_FailsAndLeavesNoFolder()
    {
        var archive = CreateTarGz("package", "tool");
        var downloader = CreateDownloader(TgzUrl, archive);
        var wrongHash = new string('0', 128);
        var download = new DependencyDownload(TgzUrl, new DependencyHash(DependencyHashAlgorithm.Sha512, wrongHash), _folderPath);

        var result = await downloader.DownloadAsync(download);

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain(wrongHash);
        ListFolder(_parentFolderPath).Should().BeEmpty();
    }

    [Test]
    public async Task Download_AnArchiveWithAnotherExtension_Fails()
    {
        var url = "https://example.com/releases/1.2.3/tool.7z";
        var archive = CreateZip("tool.exe");
        var downloader = CreateDownloader(url, archive);

        var result = await downloader.DownloadAsync(new DependencyDownload(url, HashOf(archive), _folderPath));

        result.IsFailure.Should().BeTrue();
        ListFolder(_parentFolderPath).Should().BeEmpty();
    }

    [Test]
    public async Task Download_AnArchiveThatFailsToExtract_LeavesNoFolder()
    {
        var archive = new byte[]
        {
            1, 2, 3, 4, 5,
        };
        var downloader = CreateDownloader(ZipUrl, archive);

        var result = await downloader.DownloadAsync(new DependencyDownload(ZipUrl, HashOf(archive), _folderPath));

        result.IsFailure.Should().BeTrue();
        ListFolder(_parentFolderPath).Should().BeEmpty();
    }

    [Test]
    public async Task Download_AFailedRequest_FailsAndLeavesNoFolder()
    {
        var downloader = CreateDownloader("https://example.com/elsewhere.zip", CreateZip("tool.exe"));

        var result = await downloader.DownloadAsync(new DependencyDownload(ZipUrl, new DependencyHash(DependencyHashAlgorithm.Sha256, new string('0', 64)), _folderPath));

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("404");
        ListFolder(_parentFolderPath).Should().BeEmpty();
    }

    [Test]
    public async Task Download_AFolderThatAnotherCopyInstalledFirst_KeepsThatFolder()
    {
        Directory.CreateDirectory(_folderPath);
        File.WriteAllText(Path.Combine(_folderPath, "tool.exe"), "installed by another copy");
        var archive = CreateZip("tool.exe");
        var downloader = CreateDownloader(ZipUrl, archive);

        var result = await downloader.DownloadAsync(new DependencyDownload(ZipUrl, HashOf(archive), _folderPath));

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        File.ReadAllText(Path.Combine(_folderPath, "tool.exe")).Should().Be("installed by another copy");
        ListFolder(_parentFolderPath).Should().Equal("1.2.3");
    }

    private static DependencyDownloader CreateDownloader(string url, byte[] archive)
    {
        var fileSystem = new LocalFileSystem(new NullLogger<LocalFileSystem>());
        var handler = new SingleFileHandler(url, archive);

        return new DependencyDownloader(fileSystem, new NullLogger<DependencyDownloader>(), handler);
    }

    private static DependencyHash HashOf(byte[] bytes)
    {
        var hashText = Convert.ToHexStringLower(SHA256.HashData(bytes));

        return new DependencyHash(DependencyHashAlgorithm.Sha256, hashText);
    }

    private static DependencyHash Sha512HashOf(byte[] bytes)
    {
        var hashText = Convert.ToHexStringLower(SHA512.HashData(bytes));

        return new DependencyHash(DependencyHashAlgorithm.Sha512, hashText);
    }

    private static IReadOnlyList<string> ListFolder(string folderPath)
    {
        return Directory.EnumerateFileSystemEntries(folderPath)
            .Select(path => Path.GetFileName(path))
            .ToList();
    }

    private static byte[] CreateZip(params string[] fileNames)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var fileName in fileNames)
            {
                var entry = zip.CreateEntry(fileName);
                using var entryStream = entry.Open();
                entryStream.Write(new byte[]
                {
                    42,
                });
            }
        }

        return stream.ToArray();
    }

    // The archive's binaries sit in one folder at the archive's root, with the executable mode.
    private static byte[] CreateTarGz(string folderName, string fileName)
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip))
        {
            var folderEntry = new PaxTarEntry(TarEntryType.Directory, $"{folderName}/");
            tar.WriteEntry(folderEntry);

            var fileEntry = new PaxTarEntry(TarEntryType.RegularFile, $"{folderName}/{fileName}")
            {
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute,
                DataStream = new MemoryStream(new byte[]
                {
                    42,
                })
            };
            tar.WriteEntry(fileEntry);
        }

        return stream.ToArray();
    }

    // Answers one address with the archive, and every other address with 404.
    private sealed class SingleFileHandler : HttpMessageHandler
    {
        private readonly string _url;
        private readonly byte[] _content;

        public SingleFileHandler(string url, byte[] content)
        {
            _url = url;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.ToString() != _url)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_content)
            };

            return Task.FromResult(response);
        }
    }
}
