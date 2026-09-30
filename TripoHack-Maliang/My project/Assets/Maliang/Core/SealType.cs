namespace Maliang.Core
{
    /// <summary>The player's intent, chosen by which seal they stamp. Data, not something the AI has to see (TechPlan §5.1).</summary>
    public enum SealType
    {
        /// <summary>「物」 — make the drawn thing: Tripo pipeline.</summary>
        Object,
        /// <summary>「境」 — make the drawn place: World Labs pipeline.</summary>
        World,
    }
}
