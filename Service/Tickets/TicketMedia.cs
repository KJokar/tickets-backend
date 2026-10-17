using System.Text;
using Database.Model.Tickets;
using ViewModels.Tickets;

namespace Service.Tickets
{
    internal static class TicketMedia
    {
        public static string? Validate(IReadOnlyList<UploadedTicketMedia> files)
        {
            if (files.Count > TicketMediaLimits.MaxFiles)
                return "حداکثر ۵ فایل می‌توانید پیوست کنید.";
            if (files.Sum(file => (long)file.Content.Length) > TicketMediaLimits.MaxTotalBytes)
                return "مجموع حجم پیوست‌ها نباید بیشتر از ۲۰ مگابایت باشد.";
            foreach (UploadedTicketMedia file in files)
            {
                if (file.Content.Length == 0) return "فایل خالی قابل ارسال نیست.";
                if (ContentType(file) == null) return "فرمت فایل پشتیبانی نمی‌شود. عکس، ویدئو، صدا یا PDF انتخاب کنید.";
                if (string.IsNullOrWhiteSpace(SafeName(file.FileName))) return "نام فایل معتبر نیست.";
            }
            return null;
        }

        public static MessageAttachment ToEntity(UploadedTicketMedia file) => new()
        {
            FileName = SafeName(file.FileName), ContentType = ContentType(file)!,
            Content = file.Content, Size = file.Content.Length
        };

        private static string SafeName(string name)
        {
            string value = Path.GetFileName(name.Replace('\\', '/'));
            value = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
            return value.Length <= 200 ? value : value[..200];
        }

        // Use the file signature rather than the browser's claimed MIME type.
        private static string? ContentType(UploadedTicketMedia file)
        {
            ReadOnlySpan<byte> data = file.Content;
            if (data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
            if (data.StartsWith(new byte[] { 255, 216, 255 })) return "image/jpeg";
            if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8)) return "image/gif";
            if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8))
            {
                if (data.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
                if (data.Slice(8, 4).SequenceEqual("WAVE"u8)) return "audio/wav";
            }
            if (data.StartsWith("%PDF-"u8)) return "application/pdf";
            if (data.StartsWith("OggS"u8)) return "audio/ogg";
            if (data.StartsWith("ID3"u8) || data.Length >= 2 && data[0] == 255 && (data[1] & 0xE0) == 0xE0)
                return "audio/mpeg";
            if (data.StartsWith(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }))
                return Path.GetExtension(file.FileName).Equals(".weba", StringComparison.OrdinalIgnoreCase) ? "audio/webm" : "video/webm";
            if (data.Length >= 12 && data.Slice(4, 4).SequenceEqual("ftyp"u8))
            {
                string brand = Encoding.ASCII.GetString(data.Slice(8, 4));
                return brand switch
                {
                    "avif" or "avis" => "image/avif",
                    "heic" or "heix" or "hevc" or "hevx" => "image/heic",
                    "mif1" or "msf1" => "image/heif",
                    "M4A " or "M4B " => "audio/mp4",
                    "qt  " => "video/quicktime",
                    _ => "video/mp4"
                };
            }
            return null;
        }
    }
}
