namespace KnowledgeBaseQaAgent.Desktop.Services;

public static class RagQueryClassifier
{
    private static readonly string[] BroadTerms = ["所有", "全部", "全量", "完整", "一共", "汇总", "列表", "清单", "有哪些", "哪些"];
    private static readonly string[] InventoryTerms = ["专业", "课程", "项目", "业务", "材料", "流程", "部门", "窗口", "费用", "证件", "政策", "服务"];

    public static bool IsBroadInventoryQuestion(string question)
    {
        var normalized = (question ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var hasBroadTerm = BroadTerms.Any(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase));
        var hasInventoryTerm = InventoryTerms.Any(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase));
        return hasBroadTerm && hasInventoryTerm;
    }

    public static int ResolveRetrievalLimit(string question, int configuredTopK)
    {
        var topK = Math.Clamp(configuredTopK <= 0 ? 12 : configuredTopK, 1, 50);
        return IsBroadInventoryQuestion(question)
            ? Math.Max(topK, 24)
            : topK;
    }
}
