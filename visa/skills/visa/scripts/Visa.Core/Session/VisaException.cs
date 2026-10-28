using System.Text;

namespace Visa.Core.Session;

public class VisaException : Exception
{
    public uint Vi { get; }
    public int Status { get; }
    public string Operation { get; }
    public string Description { get; }

    internal VisaException(uint vi, int status, string operation)
        : base($"{operation} failed")
    {
        Vi = vi;
        Status = status;
        Operation = operation;
        Description = FetchDescription(vi, status);
    }

    private static string FetchDescription(uint vi, int status)
    {
        var desc = new StringBuilder(256);
        int descStatus = VisaNative.viStatusDesc(vi, status, desc);
        return descStatus >= 0 ? desc.ToString() : $"status 0x{(uint)status:X8}";
    }
}
