using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindMap.Api.Domain.Entities;

namespace MindMap.Api.Infrastructure.Data;

/// <summary>
/// 系统内置模板种子数据。
/// 首次部署时自动写入 8 套推荐模板，用户可在管理后台进一步调整。
/// </summary>
public static class TemplateSeedData
{
    /// <summary>
    /// 确保模板表中有内置模板。如果表为空，则写入全部 8 套推荐模板。
    /// </summary>
    public static async Task EnsureTemplatesAsync(AppDbContext db)
    {
        if (await db.Templates.AnyAsync())
            return;

        var now = DateTime.UtcNow;
        var templates = BuildSeedTemplates(now);
        db.Templates.AddRange(templates);
        await db.SaveChangesAsync();
    }

    private static List<Template> BuildSeedTemplates(DateTime now)
    {
        return new List<Template>
        {
            // 1. 项目规划模板 - 蓝色商务
            new()
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "项目规划模板",
                Description = "蓝色商务风格，适用于项目启动、任务拆解和进度规划",
                SortOrder = 1,
                IsEnabled = true,
                ConfigJson = BuildConfigJson(new ThemeColorOptions
                {
                    RootFill = "#2563eb",
                    RootColor = "#ffffff",
                    SecondFill = "#dbeafe",
                    SecondColor = "#1e40af",
                    SecondBorder = "#60a5fa",
                    NodeColor = "#3b82f6",
                    LineColor = "#3b82f6",
                    BgColor = "#eff6ff",
                    LineStyle = "curve"
                }),
                InitialStructureJson = BuildProjectPlanStructure(),
                SwatchJson = @"{""rootFill"":""#2563eb"",""secondFill"":""#dbeafe"",""lineColor"":""#3b82f6"",""bg"":""#eff6ff""}",
                CreatedAt = now,
                UpdatedAt = now
            },
            // 2. SWOT 分析模板 - 森林绿
            new()
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "SWOT分析模板",
                Description = "森林绿专业风格，战略分析、竞品分析、自我诊断必备",
                SortOrder = 2,
                IsEnabled = true,
                ConfigJson = BuildConfigJson(new ThemeColorOptions
                {
                    RootFill = "#15803d",
                    RootColor = "#ffffff",
                    SecondFill = "#dcfce7",
                    SecondColor = "#166534",
                    SecondBorder = "#4ade80",
                    NodeColor = "#15803d",
                    LineColor = "#22c55e",
                    BgColor = "#f0fdf4",
                    LineStyle = "straight"
                }),
                InitialStructureJson = BuildSwotStructure(),
                SwatchJson = @"{""rootFill"":""#15803d"",""secondFill"":""#dcfce7"",""lineColor"":""#22c55e"",""bg"":""#f0fdf4""}",
                CreatedAt = now,
                UpdatedAt = now
            },
            // 3. 会议纪要模板 - 灰色沉稳
            new()
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "会议纪要模板",
                Description = "灰色专业风格，快速记录会议要点、决议和行动项",
                SortOrder = 3,
                IsEnabled = true,
                ConfigJson = BuildConfigJson(new ThemeColorOptions
                {
                    RootFill = "#475569",
                    RootColor = "#ffffff",
                    SecondFill = "#f1f5f9",
                    SecondColor = "#334155",
                    SecondBorder = "#94a3b8",
                    NodeColor = "#64748b",
                    LineColor = "#64748b",
                    BgColor = "#f8fafc",
                    LineStyle = "straight"
                }),
                InitialStructureJson = BuildMeetingStructure(),
                SwatchJson = @"{""rootFill"":""#475569"",""secondFill"":""#f1f5f9"",""lineColor"":""#64748b"",""bg"":""#f8fafc""}",
                CreatedAt = now,
                UpdatedAt = now
            },
            // 4. 读书笔记模板 - 暖橙温馨
            new()
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Name = "读书笔记模板",
                Description = "暖橙温馨风格，深度阅读整理、知识内化的好帮手",
                SortOrder = 4,
                IsEnabled = true,
                ConfigJson = BuildConfigJson(new ThemeColorOptions
                {
                    RootFill = "#d97706",
                    RootColor = "#ffffff",
                    SecondFill = "#fef3c7",
                    SecondColor = "#92400e",
                    SecondBorder = "#fbbf24",
                    NodeColor = "#b45309",
                    LineColor = "#f59e0b",
                    BgColor = "#fffbeb",
                    LineStyle = "curve"
                }),
                InitialStructureJson = BuildReadingNoteStructure(),
                SwatchJson = @"{""rootFill"":""#d97706"",""secondFill"":""#fef3c7"",""lineColor"":""#f59e0b"",""bg"":""#fffbeb""}",
                CreatedAt = now,
                UpdatedAt = now
            },
            // 5. 头脑风暴模板 - 紫色创想
            new()
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                Name = "头脑风暴模板",
                Description = "紫色创想风格，发散思维、激发灵感、自由联想专用",
                SortOrder = 5,
                IsEnabled = true,
                ConfigJson = BuildConfigJson(new ThemeColorOptions
                {
                    RootFill = "#7c3aed",
                    RootColor = "#ffffff",
                    SecondFill = "#ede9fe",
                    SecondColor = "#5b21b6",
                    SecondBorder = "#a78bfa",
                    NodeColor = "#7c3aed",
                    LineColor = "#8b5cf6",
                    BgColor = "#faf5ff",
                    LineStyle = "curve"
                }),
                InitialStructureJson = BuildBrainstormStructure(),
                SwatchJson = @"{""rootFill"":""#7c3aed"",""secondFill"":""#ede9fe"",""lineColor"":""#8b5cf6"",""bg"":""#faf5ff""}",
                CreatedAt = now,
                UpdatedAt = now
            },
            // 6. 周计划模板 - 青色清爽
            new()
            {
                Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                Name = "周计划模板",
                Description = "青色清爽风格，每周规划、目标拆解、习惯追踪",
                SortOrder = 6,
                IsEnabled = true,
                ConfigJson = BuildConfigJson(new ThemeColorOptions
                {
                    RootFill = "#0891b2",
                    RootColor = "#ffffff",
                    SecondFill = "#cffafe",
                    SecondColor = "#155e75",
                    SecondBorder = "#22d3ee",
                    NodeColor = "#0e7490",
                    LineColor = "#06b6d4",
                    BgColor = "#ecfeff",
                    LineStyle = "straight"
                }),
                InitialStructureJson = BuildWeeklyPlanStructure(),
                SwatchJson = @"{""rootFill"":""#0891b2"",""secondFill"":""#cffafe"",""lineColor"":""#06b6d4"",""bg"":""#ecfeff""}",
                CreatedAt = now,
                UpdatedAt = now
            },
            // 7. 产品需求模板 - 靛蓝专业
            new()
            {
                Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                Name = "产品需求模板",
                Description = "靛蓝专业风格，PRD梳理、功能拆解、需求评审",
                SortOrder = 7,
                IsEnabled = true,
                ConfigJson = BuildConfigJson(new ThemeColorOptions
                {
                    RootFill = "#4338ca",
                    RootColor = "#ffffff",
                    SecondFill = "#e0e7ff",
                    SecondColor = "#3730a3",
                    SecondBorder = "#818cf8",
                    NodeColor = "#4f46e5",
                    LineColor = "#6366f1",
                    BgColor = "#eef2ff",
                    LineStyle = "straight"
                }),
                InitialStructureJson = BuildProductRequirementStructure(),
                SwatchJson = @"{""rootFill"":""#4338ca"",""secondFill"":""#e0e7ff"",""lineColor"":""#6366f1"",""bg"":""#eef2ff""}",
                CreatedAt = now,
                UpdatedAt = now
            },
            // 8. 个人成长模板 - 樱粉温暖
            new()
            {
                Id = Guid.Parse("88888888-8888-8888-8888-888888888888"),
                Name = "个人成长模板",
                Description = "樱粉温暖风格，年度规划、自我提升、目标追踪",
                SortOrder = 8,
                IsEnabled = true,
                ConfigJson = BuildConfigJson(new ThemeColorOptions
                {
                    RootFill = "#db2777",
                    RootColor = "#ffffff",
                    SecondFill = "#fce7f3",
                    SecondColor = "#9d174d",
                    SecondBorder = "#f472b6",
                    NodeColor = "#be185d",
                    LineColor = "#ec4899",
                    BgColor = "#fdf2f8",
                    LineStyle = "curve"
                }),
                InitialStructureJson = BuildPersonalGrowthStructure(),
                SwatchJson = @"{""rootFill"":""#db2777"",""secondFill"":""#fce7f3"",""lineColor"":""#ec4899"",""bg"":""#fdf2f8""}",
                CreatedAt = now,
                UpdatedAt = now
            }
        };
    }

    #region 主题配置构建

    private class ThemeColorOptions
    {
        public string RootFill { get; set; } = string.Empty;
        public string RootColor { get; set; } = string.Empty;
        public string SecondFill { get; set; } = string.Empty;
        public string SecondColor { get; set; } = string.Empty;
        public string SecondBorder { get; set; } = string.Empty;
        public string NodeColor { get; set; } = string.Empty;
        public string LineColor { get; set; } = string.Empty;
        public string BgColor { get; set; } = string.Empty;
        public string LineStyle { get; set; } = "curve";
    }

    /// <summary>
    /// 构建完整的 MindMapThemeConfig JSON 字符串。
    /// 结构与前端 presets.ts 中的 buildBaseTheme 保持一致。
    /// </summary>
    private static string BuildConfigJson(ThemeColorOptions opts)
    {
        var config = new
        {
            paddingX = 15,
            paddingY = 5,
            imgMaxWidth = 200,
            imgMaxHeight = 100,
            iconSize = 20,
            lineWidth = 1.5,
            lineColor = opts.LineColor,
            lineDasharray = "none",
            lineFlow = false,
            lineFlowDuration = 1,
            lineFlowForward = true,
            lineStyle = opts.LineStyle,
            rootLineKeepSameInCurve = true,
            rootLineStartPositionKeepSameInCurve = false,
            lineRadius = 5,
            showLineMarker = false,
            generalizationLineWidth = 1,
            generalizationLineColor = opts.LineColor,
            generalizationLineMargin = 0,
            generalizationNodeMargin = 20,
            associativeLineWidth = 2,
            associativeLineColor = opts.LineColor,
            associativeLineActiveWidth = 8,
            associativeLineActiveColor = "#02a7f0",
            associativeLineDasharray = "6,4",
            associativeLineTextColor = opts.NodeColor,
            associativeLineTextFontSize = 14,
            associativeLineTextLineHeight = 1.2,
            associativeLineTextFontFamily = "微软雅黑, Microsoft YaHei",
            backgroundColor = opts.BgColor,
            backgroundImage = "none",
            backgroundRepeat = "no-repeat",
            backgroundPosition = "center center",
            backgroundSize = "cover",
            nodeUseLineStyle = false,
            root = new
            {
                shape = "rectangle",
                fontFamily = "微软雅黑, Microsoft YaHei",
                fontSize = 16,
                fontWeight = "bold",
                fontStyle = "normal",
                borderWidth = 0,
                borderDasharray = "none",
                borderRadius = 5,
                textDecoration = "none",
                gradientStyle = false,
                startColor = opts.RootFill,
                endColor = "#fff",
                startDir = new[] { 0, 0 },
                endDir = new[] { 1, 0 },
                lineMarkerDir = "end",
                hoverRectColor = "",
                hoverRectRadius = 5,
                textAlign = "left",
                imgPlacement = "top",
                tagPlacement = "right",
                marginX = 0,
                marginY = 0,
                fillColor = opts.RootFill,
                color = opts.RootColor,
                borderColor = "transparent"
            },
            second = new
            {
                shape = "rectangle",
                fontFamily = "微软雅黑, Microsoft YaHei",
                fontSize = 15,
                fontWeight = "normal",
                fontStyle = "normal",
                borderWidth = 1,
                borderDasharray = "none",
                borderRadius = 5,
                textDecoration = "none",
                gradientStyle = false,
                startColor = "#549688",
                endColor = "#fff",
                startDir = new[] { 0, 0 },
                endDir = new[] { 1, 0 },
                lineMarkerDir = "end",
                hoverRectColor = "",
                hoverRectRadius = 5,
                textAlign = "left",
                imgPlacement = "top",
                tagPlacement = "right",
                marginX = 100,
                marginY = 40,
                fillColor = opts.SecondFill,
                color = opts.SecondColor,
                borderColor = opts.SecondBorder
            },
            node = new
            {
                shape = "rectangle",
                fontFamily = "微软雅黑, Microsoft YaHei",
                fontSize = 14,
                fontWeight = "normal",
                fontStyle = "normal",
                borderWidth = 0,
                borderDasharray = "none",
                borderRadius = 5,
                textDecoration = "none",
                gradientStyle = false,
                startColor = "#549688",
                endColor = "#fff",
                startDir = new[] { 0, 0 },
                endDir = new[] { 1, 0 },
                lineMarkerDir = "end",
                hoverRectColor = "",
                hoverRectRadius = 5,
                textAlign = "left",
                imgPlacement = "top",
                tagPlacement = "right",
                marginX = 50,
                marginY = 0,
                fillColor = "transparent",
                color = opts.NodeColor,
                borderColor = "transparent"
            },
            generalization = new
            {
                shape = "rectangle",
                fontFamily = "微软雅黑, Microsoft YaHei",
                fontSize = 15,
                fontWeight = "normal",
                fontStyle = "normal",
                borderWidth = 1,
                borderDasharray = "none",
                borderRadius = 5,
                textDecoration = "none",
                gradientStyle = false,
                startColor = "#549688",
                endColor = "#fff",
                startDir = new[] { 0, 0 },
                endDir = new[] { 1, 0 },
                lineMarkerDir = "end",
                hoverRectColor = "",
                hoverRectRadius = 5,
                textAlign = "left",
                imgPlacement = "top",
                tagPlacement = "right",
                marginX = 100,
                marginY = 40,
                fillColor = opts.SecondFill,
                color = opts.SecondColor,
                borderColor = opts.SecondBorder
            }
        };

        return JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    #endregion

    #region 初始结构构建

    // 以下结构均遵循 simple-mind-map 的 data 格式：{ data: { text: "..." }, children: [...] }

    private static string BuildProjectPlanStructure()
    {
        var tree = new
        {
            data = new { text = "项目名称" },
            children = new object[]
            {
                new
                {
                    data = new { text = "项目背景与目标" },
                    children = new object[]
                    {
                        new { data = new { text = "项目背景" } },
                        new { data = new { text = "预期目标" } }
                    }
                },
                new
                {
                    data = new { text = "范围与交付物" },
                    children = new object[]
                    {
                        new { data = new { text = "包含范围" } },
                        new { data = new { text = "交付物清单" } }
                    }
                },
                new
                {
                    data = new { text = "里程碑计划" },
                    children = new object[]
                    {
                        new { data = new { text = "阶段一" } },
                        new { data = new { text = "阶段二" } },
                        new { data = new { text = "阶段三" } }
                    }
                },
                new
                {
                    data = new { text = "团队与分工" },
                    children = new object[]
                    {
                        new { data = new { text = "负责人" } },
                        new { data = new { text = "成员角色" } }
                    }
                },
                new
                {
                    data = new { text = "风险与应对" },
                    children = new object[]
                    {
                        new { data = new { text = "已知风险" } },
                        new { data = new { text = "应对措施" } }
                    }
                }
            }
        };
        return JsonSerializer.Serialize(tree);
    }

    private static string BuildSwotStructure()
    {
        var tree = new
        {
            data = new { text = "SWOT 分析" },
            children = new object[]
            {
                new
                {
                    data = new { text = "S 优势 (Strengths)" },
                    children = new object[]
                    {
                        new { data = new { text = "核心优势 1" } },
                        new { data = new { text = "核心优势 2" } },
                        new { data = new { text = "核心优势 3" } }
                    }
                },
                new
                {
                    data = new { text = "W 劣势 (Weaknesses)" },
                    children = new object[]
                    {
                        new { data = new { text = "待改进 1" } },
                        new { data = new { text = "待改进 2" } },
                        new { data = new { text = "待改进 3" } }
                    }
                },
                new
                {
                    data = new { text = "O 机会 (Opportunities)" },
                    children = new object[]
                    {
                        new { data = new { text = "外部机会 1" } },
                        new { data = new { text = "外部机会 2" } },
                        new { data = new { text = "外部机会 3" } }
                    }
                },
                new
                {
                    data = new { text = "T 威胁 (Threats)" },
                    children = new object[]
                    {
                        new { data = new { text = "潜在威胁 1" } },
                        new { data = new { text = "潜在威胁 2" } },
                        new { data = new { text = "潜在威胁 3" } }
                    }
                }
            }
        };
        return JsonSerializer.Serialize(tree);
    }

    private static string BuildMeetingStructure()
    {
        var tree = new
        {
            data = new { text = "会议主题" },
            children = new object[]
            {
                new
                {
                    data = new { text = "基本信息" },
                    children = new object[]
                    {
                        new { data = new { text = "时间" } },
                        new { data = new { text = "地点" } },
                        new { data = new { text = "主持人" } }
                    }
                },
                new
                {
                    data = new { text = "参会人员" },
                    children = new object[]
                    {
                        new { data = new { text = "出席" } },
                        new { data = new { text = "缺席" } }
                    }
                },
                new
                {
                    data = new { text = "讨论议题" },
                    children = new object[]
                    {
                        new { data = new { text = "议题一" } },
                        new { data = new { text = "议题二" } }
                    }
                },
                new
                {
                    data = new { text = "会议决议" },
                    children = new object[]
                    {
                        new { data = new { text = "决议一" } },
                        new { data = new { text = "决议二" } }
                    }
                },
                new
                {
                    data = new { text = "待办事项" },
                    children = new object[]
                    {
                        new { data = new { text = "任务一 · 负责人 · DDL" } },
                        new { data = new { text = "任务二 · 负责人 · DDL" } }
                    }
                }
            }
        };
        return JsonSerializer.Serialize(tree);
    }

    private static string BuildReadingNoteStructure()
    {
        var tree = new
        {
            data = new { text = "《书名》读书笔记" },
            children = new object[]
            {
                new
                {
                    data = new { text = "书籍信息" },
                    children = new object[]
                    {
                        new { data = new { text = "作者" } },
                        new { data = new { text = "出版社" } },
                        new { data = new { text = "阅读日期" } }
                    }
                },
                new
                {
                    data = new { text = "核心观点" },
                    children = new object[]
                    {
                        new { data = new { text = "观点一" } },
                        new { data = new { text = "观点二" } },
                        new { data = new { text = "观点三" } }
                    }
                },
                new
                {
                    data = new { text = "章节摘要" },
                    children = new object[]
                    {
                        new { data = new { text = "第一章" } },
                        new { data = new { text = "第二章" } },
                        new { data = new { text = "第三章" } }
                    }
                },
                new
                {
                    data = new { text = "金句摘录" },
                    children = new object[]
                    {
                        new { data = new { text = "摘录一" } },
                        new { data = new { text = "摘录二" } }
                    }
                },
                new
                {
                    data = new { text = "行动清单" },
                    children = new object[]
                    {
                        new { data = new { text = "立即行动" } },
                        new { data = new { text = "长期改变" } }
                    }
                }
            }
        };
        return JsonSerializer.Serialize(tree);
    }

    private static string BuildBrainstormStructure()
    {
        var tree = new
        {
            data = new { text = "头脑风暴主题" },
            children = new object[]
            {
                new
                {
                    data = new { text = "方向一" },
                    children = new object[]
                    {
                        new { data = new { text = "想法 1" } },
                        new { data = new { text = "想法 2" } },
                        new { data = new { text = "想法 3" } }
                    }
                },
                new
                {
                    data = new { text = "方向二" },
                    children = new object[]
                    {
                        new { data = new { text = "想法 1" } },
                        new { data = new { text = "想法 2" } },
                        new { data = new { text = "想法 3" } }
                    }
                },
                new
                {
                    data = new { text = "方向三" },
                    children = new object[]
                    {
                        new { data = new { text = "想法 1" } },
                        new { data = new { text = "想法 2" } },
                        new { data = new { text = "想法 3" } }
                    }
                },
                new
                {
                    data = new { text = "疯狂想法" },
                    children = new object[]
                    {
                        new { data = new { text = "不设限，大胆想" } }
                    }
                },
                new
                {
                    data = new { text = "可行方案筛选" },
                    children = new object[]
                    {
                        new { data = new { text = "评估优先级" } }
                    }
                }
            }
        };
        return JsonSerializer.Serialize(tree);
    }

    private static string BuildWeeklyPlanStructure()
    {
        var tree = new
        {
            data = new { text = "本周计划" },
            children = new object[]
            {
                new
                {
                    data = new { text = "本周核心目标" },
                    children = new object[]
                    {
                        new { data = new { text = "目标 1" } },
                        new { data = new { text = "目标 2" } }
                    }
                },
                new
                {
                    data = new { text = "工作任务" },
                    children = new object[]
                    {
                        new { data = new { text = "重要紧急" } },
                        new { data = new { text = "重要不紧急" } },
                        new { data = new { text = "日常事务" } }
                    }
                },
                new
                {
                    data = new { text = "学习成长" },
                    children = new object[]
                    {
                        new { data = new { text = "阅读" } },
                        new { data = new { text = "课程" } },
                        new { data = new { text = "技能练习" } }
                    }
                },
                new
                {
                    data = new { text = "生活健康" },
                    children = new object[]
                    {
                        new { data = new { text = "运动计划" } },
                        new { data = new { text = "饮食安排" } },
                        new { data = new { text = "作息规律" } }
                    }
                },
                new
                {
                    data = new { text = "周末回顾" },
                    children = new object[]
                    {
                        new { data = new { text = "完成情况" } },
                        new { data = new { text = "下周展望" } }
                    }
                }
            }
        };
        return JsonSerializer.Serialize(tree);
    }

    private static string BuildProductRequirementStructure()
    {
        var tree = new
        {
            data = new { text = "产品需求文档" },
            children = new object[]
            {
                new
                {
                    data = new { text = "需求背景" },
                    children = new object[]
                    {
                        new { data = new { text = "业务目标" } },
                        new { data = new { text = "用户痛点" } },
                        new { data = new { text = "预期收益" } }
                    }
                },
                new
                {
                    data = new { text = "功能需求" },
                    children = new object[]
                    {
                        new
                        {
                            data = new { text = "模块一" },
                            children = new object[]
                            {
                                new { data = new { text = "子功能" } }
                            }
                        },
                        new
                        {
                            data = new { text = "模块二" },
                            children = new object[]
                            {
                                new { data = new { text = "子功能" } }
                            }
                        },
                        new { data = new { text = "模块三" } }
                    }
                },
                new
                {
                    data = new { text = "非功能需求" },
                    children = new object[]
                    {
                        new { data = new { text = "性能要求" } },
                        new { data = new { text = "安全要求" } },
                        new { data = new { text = "兼容性" } }
                    }
                },
                new
                {
                    data = new { text = "优先级排期" },
                    children = new object[]
                    {
                        new { data = new { text = "P0 必须" } },
                        new { data = new { text = "P1 重要" } },
                        new { data = new { text = "P2 一般" } }
                    }
                },
                new
                {
                    data = new { text = "风险与依赖" },
                    children = new object[]
                    {
                        new { data = new { text = "技术风险" } },
                        new { data = new { text = "外部依赖" } }
                    }
                }
            }
        };
        return JsonSerializer.Serialize(tree);
    }

    private static string BuildPersonalGrowthStructure()
    {
        var tree = new
        {
            data = new { text = "年度成长规划" },
            children = new object[]
            {
                new
                {
                    data = new { text = "职业发展" },
                    children = new object[]
                    {
                        new { data = new { text = "年度目标" } },
                        new { data = new { text = "关键成果" } },
                        new { data = new { text = "行动计划" } }
                    }
                },
                new
                {
                    data = new { text = "技能提升" },
                    children = new object[]
                    {
                        new { data = new { text = "专业技能" } },
                        new { data = new { text = "软技能" } },
                        new { data = new { text = "兴趣爱好" } }
                    }
                },
                new
                {
                    data = new { text = "身心健康" },
                    children = new object[]
                    {
                        new { data = new { text = "运动计划" } },
                        new { data = new { text = "心理健康" } },
                        new { data = new { text = "作息饮食" } }
                    }
                },
                new
                {
                    data = new { text = "人际关系" },
                    children = new object[]
                    {
                        new { data = new { text = "家庭" } },
                        new { data = new { text = "朋友" } },
                        new { data = new { text = "社交圈" } }
                    }
                },
                new
                {
                    data = new { text = "财务规划" },
                    children = new object[]
                    {
                        new { data = new { text = "收入目标" } },
                        new { data = new { text = "储蓄计划" } },
                        new { data = new { text = "投资理财" } }
                    }
                }
            }
        };
        return JsonSerializer.Serialize(tree);
    }

    #endregion
}

