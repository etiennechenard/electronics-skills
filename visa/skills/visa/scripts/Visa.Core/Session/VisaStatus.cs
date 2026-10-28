namespace Visa.Core.Session;

internal static class VisaStatus
{
    internal const int Success             = 0x00000000;
    internal const int SuccessTermChar     = unchecked((int)0x3FFF0005); // VI_SUCCESS_TERM_CHAR
    internal const int SuccessMaxCnt       = unchecked((int)0x3FFF0006); // VI_SUCCESS_MAX_CNT
    internal const int ErrorTmo            = unchecked((int)0xBFFF0015); // VI_ERROR_TMO
    internal const int ErrorRsrcNotFound   = unchecked((int)0xBFFF0011); // VI_ERROR_RSRC_NFOUND
    internal const int ErrorInvRsrcName    = unchecked((int)0xBFFF001C); // VI_ERROR_INV_RSRC_NAME
    internal const int ErrorNsupOper       = unchecked((int)0xBFFF0067); // VI_ERROR_NSUP_OPER

    internal static bool IsError(int status) => status < 0;
    internal static bool IsWarning(int status) => status > 0;

    internal static void Throw(uint vi, int status, string operation)
    {
        if (IsError(status))
            throw new VisaException(vi, status, operation);
    }
}
