namespace Game.Sdk
{
    /// <summary>
    /// 所有 SDK 回调的公共载体。
    /// 底层 SDK 后续多返回字段时只加成员，不改接口签名，也不动任何调用点。
    /// </summary>
    public abstract class SdkEventArgs
    {
        public bool   Success;   // 通常等价于 Code == 0
        public int    Code;
        public string Message;

        /// <summary>
        /// 原始返回数据。故意用 string 而非第三方 JSON 类型，避免类型外泄。
        /// </summary>
        public string RawJson;
    }
}
