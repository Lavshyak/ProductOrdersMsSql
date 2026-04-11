using System.Data.SqlTypes;

namespace Shared;

public static class GuidExtensions
{
    /// <summary>
    /// maybe
    /// </summary>
    public static bool IsV7(this Guid guid)
    {
        return guid.Version == 7;
    }
    
    /// <summary>
    /// maybe
    /// </summary>
    public static bool IsV7SwappedForMsSql(this Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes);

        // 6 (normal guid) -> 7 (WriteBytes) -> 6 (reorder for internal) -> 8 (reorder for SQL SERVER)
        
        return bytes[8] >>> 4 == 7;
    }

    private static void ReorderBytesForGuidInternalLayout(Span<byte> src)
    {
        var tmp0 = src[0];
        var tmp1 = src[1];
        var tmp2 = src[2];
        var tmp3 = src[3];
        src[0] = tmp3;
        src[1] = tmp2;
        src[2] = tmp1;
        src[3] = tmp0;
        var tmp4 = src[4];
        var tmp5 = src[5];
        src[4] = tmp5;
        src[5] = tmp4;
        var tmp6 = src[6];
        var tmp7 = src[7];
        src[6] = tmp7;
        src[7] = tmp6;
    }

    private static Guid SwapToMsSqlServer(this Guid guid)
    {
        Span<byte> src = stackalloc byte[16];
        guid.TryWriteBytes(src);

        // reorder because TryWriteBytes writes internal layout of .net Guid as is
        ReorderBytesForGuidInternalLayout(src);
        
        Span<byte> dst = stackalloc byte[16];
        // reorder for SQL SERVER Sort order but without taking into account group endiannesses
        dst[0] = src[12];
        dst[1] = src[13];
        dst[2] = src[14];
        dst[3] = src[15];
        
        dst[4] = src[10];
        dst[5] = src[11];
        
        dst[6] = src[8];
        dst[7] = src[9];
        
        dst[8] = src[6];
        dst[9] = src[7];
        
        dst[10] = src[0];
        dst[11] = src[1];
        dst[12] = src[2];
        dst[13] = src[3];
        dst[14] = src[4];
        dst[15] = src[5];
        
        // reorder for SQL SERVER group endianness. It's a coincidence that this method works here too
        ReorderBytesForGuidInternalLayout(dst);
        
        // reorder because new Guid(bytes) writes to internal layout of .net Guid as is
        ReorderBytesForGuidInternalLayout(dst);
        
        return new Guid(dst);
    }

    private static Guid SwapFromMsSqlServer(this Guid guid)
    {
        Span<byte> src = stackalloc byte[16];
        guid.TryWriteBytes(src);
        
        ReorderBytesForGuidInternalLayout(src);
        
        // reorder from SQL SERVER group endiannesses.
        ReorderBytesForGuidInternalLayout(src);
        
        Span<byte> dst = stackalloc byte[16];
        
        // reorder from SQL SERVER Sort order but without taking into account group endiannesses
        dst[0] = src[10];
        dst[1] = src[11];
        dst[2] = src[12];
        dst[3] = src[13];
        
        dst[4] = src[14];
        dst[5] = src[15];
        
        dst[6] = src[8];
        dst[7] = src[9];
        
        dst[8] = src[6];
        dst[9] = src[7];
        
        dst[10] = src[4];
        dst[11] = src[5];
        
        dst[12] = src[0];
        dst[13] = src[1];
        dst[14] = src[2];
        dst[15] = src[3];


        ReorderBytesForGuidInternalLayout(dst);
        
        var newGuid = new Guid(dst);
        return newGuid;
    }

    public static Guid SwapV7ToMSS(this Guid guidV7)
    {
        if (!guidV7.IsV7())
        {
            throw new ArgumentException("The specified GUID does not match 7 version.");
        }

        return SwapToMsSqlServer(guidV7);
    }
    
    public static Guid SwapV7FromMSS(this Guid swappedGuidV7)
    {
        var newGuid = SwapFromMsSqlServer(swappedGuidV7);
        
        if (!newGuid.IsV7())
        {
            throw new ArgumentException("The result GUID does not match 7 version.");
        }
        
        return newGuid;
    }
    
}