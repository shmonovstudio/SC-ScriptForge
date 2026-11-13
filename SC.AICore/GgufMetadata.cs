using System.Text;

namespace ShmonovStudio.AI;

public sealed record GgufMetadata(string Architecture, string Name, long Bytes)
{
    /// <summary>Qwen проверена с закреплённым llama.cpp. Другие архитектуры llama.cpp тоже загружаются, но без гарантии.</summary>
    public bool Verified => Architecture is "qwen35" or "qwen3" or "qwen2";
    public static GgufMetadata Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        if (reader.ReadUInt32() != 0x46554747 || reader.ReadUInt32() is not (2 or 3)) throw new InvalidDataException("Выберите GGUF v2/v3, а не архив, LoRA или safetensors.");
        var tensors = reader.ReadUInt64(); var count = reader.ReadUInt64();
        if (tensors is 0 or > 100_000 || count > 100_000) throw new InvalidDataException("Некорректный заголовок GGUF.");
        var architecture = ""; var name = Path.GetFileNameWithoutExtension(path);
        string String()
        {
            var length = reader.ReadUInt64(); if (length > 8_000_000 || reader.BaseStream.Position + (long)Math.Min(length,32_000_001UL) > 32_000_000 || length > (ulong)(reader.BaseStream.Length - reader.BaseStream.Position)) throw new InvalidDataException("Повреждённая строка GGUF.");
            var bytes = reader.ReadBytes((int)length); if ((ulong)bytes.Length != length) throw new EndOfStreamException(); return Encoding.UTF8.GetString(bytes);
        }
        void Skip(uint type, int depth = 0)
        {
            if (depth > 1) throw new InvalidDataException("Некорректный массив GGUF.");
            var size = type switch { 0 or 1 or 7 => 1, 2 or 3 => 2, 4 or 5 or 6 => 4, 10 or 11 or 12 => 8, _ => 0 };
            if (size > 0) { if (reader.BaseStream.Position + size > reader.BaseStream.Length) throw new EndOfStreamException(); reader.BaseStream.Seek(size, SeekOrigin.Current); }
            else if (type == 8) _ = String();
            else if (type == 9) { var element = reader.ReadUInt32(); var length = reader.ReadUInt64(); if (length > 2_000_000) throw new InvalidDataException("Слишком большой массив GGUF."); for (ulong i = 0; i < length; i++) Skip(element, depth + 1); }
            else throw new InvalidDataException("Неизвестный тип метаданных GGUF.");
        }
        for (ulong i = 0; i < count; i++)
        {
            if (reader.BaseStream.Position > 32_000_000) throw new InvalidDataException("Заголовок GGUF слишком велик.");
            var key = String(); var type = reader.ReadUInt32();
            if (key == "general.architecture" && type == 8) architecture = String();
            else if (key == "general.name" && type == 8) name = String();
            else Skip(type);
            if (architecture.Length > 0 && i > 12) break;
        }
        if (architecture.Length == 0) throw new InvalidDataException("В GGUF не указана архитектура модели.");
        if (architecture is "clip" or "mmproj") throw new InvalidDataException("Это проектор изображений (mmproj), а не текстовая модель. Выберите основной файл GGUF.");
        if (reader.BaseStream.Length < 1_000_000) throw new InvalidDataException("Файл GGUF неполный.");
        return new(architecture, name, reader.BaseStream.Length);
    }
}
