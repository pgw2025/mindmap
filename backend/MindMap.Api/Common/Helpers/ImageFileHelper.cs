using System.Text;

namespace MindMap.Api.Common.Helpers;

/// <summary>
/// 节点图片文件校验与尺寸探测助手（无第三方依赖，直接解析文件头）。
/// 支持格式：jpg / png / gif / webp。
/// </summary>
public static class ImageFileHelper
{
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

    /// <summary>扩展名是否在白名单内（jpg/jpeg/png/gif/webp）。</summary>
    public static bool IsAllowedExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return AllowedExtensions.Contains(ext);
    }

    /// <summary>校验文件头魔数，防止伪装扩展名的非图片文件。</summary>
    public static bool HasValidMagic(ReadOnlySpan<byte> b)
    {
        if (b.Length < 12) return false;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 &&
            b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)
            return true;

        // JPEG: FF D8 FF
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
            return true;

        // GIF: "GIF87a" / "GIF89a"
        if (b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'8')
            return true;

        // WebP: "RIFF" .... "WEBP"
        if (b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F' &&
            b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P')
            return true;

        return false;
    }

    /// <summary>
    /// 从文件头解析图片原始宽高；无法解析时返回 null（前端可用 Image.onload 兜底）。
    /// </summary>
    public static (int Width, int Height)? TryGetSize(ReadOnlySpan<byte> b)
    {
        // PNG：IHDR 块中 offset 16/20 各 4 字节大端
        if (b.Length >= 24 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
        {
            var w = ReadBe32(b, 16);
            var h = ReadBe32(b, 20);
            if (w > 0 && h > 0) return (w, h);
        }

        // JPEG：扫描段找 SOF0/1/2（FF C0/C1/C2），offset+5 高 2 字节、offset+7 宽 2 字节（大端）
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            for (int i = 2; i + 9 < b.Length;)
            {
                if (b[i] != 0xFF) { i++; continue; }
                var marker = b[i + 1];
                if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    i += 2;
                    continue;
                }
                if (marker == 0x01 || marker == 0xD0 || marker == 0xD1)
                {
                    i += 2;
                    continue;
                }
                // 数据段长度
                var len = (b[i + 2] << 8) | b[i + 3];
                if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2)
                {
                    var h = (b[i + 5] << 8) | b[i + 6];
                    var w = (b[i + 7] << 8) | b[i + 8];
                    if (w > 0 && h > 0) return (w, h);
                }
                i += 2 + len;
            }
        }

        // GIF：offset 6/8 各 2 字节小端
        if (b.Length >= 10 && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F')
        {
            var w = b[6] | (b[7] << 8);
            var h = b[8] | (b[9] << 8);
            if (w > 0 && h > 0) return (w, h);
        }

        // WebP
        if (b.Length >= 30 && b[0] == (byte)'R' && b[8] == (byte)'W')
        {
            var chunk = Encoding.ASCII.GetString(b.Slice(12, 4));
            // VP8X：offset 19/22 各 3 字节小端 +1
            if (chunk == "VP8X" && b.Length >= 25)
            {
                var w = ReadLe24(b, 19) + 1;
                var h = ReadLe24(b, 22) + 1;
                if (w > 0 && h > 0) return (w, h);
            }
            // VP8（有损）：offset 26/28 各 14 位小端
            else if (chunk == "VP8 " && b.Length >= 30 && b[20] == 0x9D && b[21] == 0x01 && b[22] == 0x2A)
            {
                var w = ((b[27] & 0x3F) << 8) | b[26];
                var h = ((b[29] & 0x3F) << 8) | b[28];
                if (w > 0 && h > 0) return (w, h);
            }
            // VP8L（无损）：offset 21/23 各 14 位小端 +1
            else if (chunk == "VP8L" && b.Length >= 25 && b[20] == 0x2F)
            {
                var w = (((b[22] & 0x3F) << 8) | b[21]) + 1;
                var h = (((b[24] & 0x3F) << 8) | b[23]) + 1;
                if (w > 0 && h > 0) return (w, h);
            }
        }

        return null;
    }

    private static int ReadBe32(ReadOnlySpan<byte> b, int offset)
        => (b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3];

    private static int ReadLe24(ReadOnlySpan<byte> b, int offset)
        => b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16);
}
