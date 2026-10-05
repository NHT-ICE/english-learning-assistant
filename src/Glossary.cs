using System;
using System.Collections.Generic;
using System.Linq;

namespace LearningIme {
    internal static class Glossary {
        // 仅匹配原句的领域词；既提示翻译，又作为悬浮窗的学习词组。
        private static readonly Dictionary<string, string> Terms = new Dictionary<string, string> {
            {"加密网格", "mesh refinement"}, {"网格加密", "mesh refinement"},
            {"网格无关性", "mesh independence"}, {"数值模拟", "numerical simulation"},
            {"计算流体力学", "computational fluid dynamics"}, {"有限体积法", "finite volume method"},
            {"有限元法", "finite element method"}, {"有限差分法", "finite difference method"},
            {"两相流", "two-phase flow"}, {"多相流", "multiphase flow"}, {"油水界面", "oil-water interface"},
            {"时间步长", "time step"}, {"边界条件", "boundary conditions"}, {"初始条件", "initial conditions"},
            {"雷诺数", "Reynolds number"}, {"残差", "residual"}, {"收敛性", "convergence"},
            {"湍流模型", "turbulence model"}, {"层流", "laminar flow"}, {"压降", "pressure drop"},
            {"油藏", "reservoir"}, {"渗透率", "permeability"}, {"孔隙度", "porosity"},
            {"毛细管压力", "capillary pressure"}, {"相对渗透率", "relative permeability"},
            {"非均质性", "heterogeneity"}, {"高含水", "high water cut"}, {"原油黏度", "crude oil viscosity"},
            {"原油粘度", "crude oil viscosity"}, {"采收率", "oil recovery factor"},
            {"提高采收率", "enhanced oil recovery"}, {"驱油效率", "oil displacement efficiency"},
            {"水驱", "water flooding"}, {"注水井", "injection well"}, {"生产井", "production well"},
            {"饱和度", "saturation"}, {"多孔介质", "porous media"}, {"实验数据", "experimental data"},
            {"守恒方程", "conservation equations"}, {"离散格式", "discretization scheme"},
            {"压力梯度", "pressure gradient"}, {"速度场", "velocity field"}, {"压力场", "pressure field"},
            {"敏感性分析", "sensitivity analysis"}, {"不确定性", "uncertainty"}
        };
        public static List<Dictionary<string, string>> Matches(string source) {
            var selected = new List<string>();
            foreach (var key in Terms.Keys.OrderByDescending(k => k.Length)) {
                if (source.Contains(key) && !selected.Any(longer => longer.Contains(key))) selected.Add(key);
            }
            return selected.Select(key => new Dictionary<string, string> { {"source", key}, {"target", Terms[key]} }).ToList();
        }
        public static string LearningLine(string source) {
            return string.Join("   ·   ", Matches(source).Take(3).Select(t => t["source"] + " → " + t["target"]));
        }
    }
}
