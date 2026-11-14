import os
import sys
import glob
import argparse
from tensorboard.backend.event_processing.event_accumulator import EventAccumulator
import matplotlib.pyplot as plt

def smooth(scalars, weight=0.6):
    """지수 이동 평균(EMA)으로 노이즈를 완화합니다."""
    last = scalars[0] if scalars else 0
    smoothed = []
    for point in scalars:
        smoothed_val = last * weight + (1 - weight) * point
        smoothed.append(smoothed_val)
        last = smoothed_val
    return smoothed

def export_run_graphs(run_id="test_02"):
    base_dir = os.path.join("results", run_id)
    if not os.path.exists(base_dir):
        print(f"Error: 디렉터리를 찾을 수 없습니다: {base_dir}")
        return

    out_dir = os.path.join(base_dir, "plots")
    os.makedirs(out_dir, exist_ok=True)

    tfevent_files = glob.glob(os.path.join(base_dir, "**", "*.tfevents*"), recursive=True)
    if not tfevent_files:
        print(f"Error: {base_dir} 내에 tfevents 파일을 찾을 수 없습니다.")
        return

    data = {}
    for filepath in tfevent_files:
        # Player / Enemy 구분
        name = "Player" if "Player" in filepath else ("Enemy" if "Enemy" in filepath else os.path.basename(os.path.dirname(filepath)))
        ea = EventAccumulator(filepath)
        ea.Reload()
        tags = ea.Tags().get("scalars", [])
        data[name] = {}
        for tag in tags:
            events = ea.Scalars(tag)
            data[name][tag] = {
                "steps": [e.step for e in events],
                "values": [e.value for e in events]
            }

    print(f"[{run_id}] 로드 완료된 에이전트: {list(data.keys())}")

    # 스타일 설정
    plt.style.use('seaborn-v0_8-whitegrid' if 'seaborn-v0_8-whitegrid' in plt.style.available else 'default')
    colors = {"Player": "#1f77b4", "Enemy": "#d62728"}

    # 1. 종합 요약 대시보드 (2x2) 생성
    fig, axes = plt.subplots(2, 2, figsize=(14, 10))

    target_metrics = [
        ("Environment/Cumulative Reward", "Cumulative Reward", "Reward", axes[0, 0]),
        ("Environment/Episode Length", "Episode Length", "Steps", axes[0, 1]),
        ("Losses/Policy Loss", "Policy Loss", "Loss", axes[1, 0]),
        ("Losses/Value Loss", "Value Loss", "Loss", axes[1, 1])
    ]

    for tag, title, ylabel, ax in target_metrics:
        has_data = False
        for agent, metrics in data.items():
            if tag in metrics and metrics[tag]["steps"]:
                has_data = True
                steps = metrics[tag]["steps"]
                raw_vals = metrics[tag]["values"]
                color = colors.get(agent, "#2ca02c")
                
                # 원본 희미하게, 스무딩 라인 진하게 표시
                ax.plot(steps, raw_vals, color=color, alpha=0.25)
                smoothed_vals = smooth(raw_vals, weight=0.8)
                ax.plot(steps, smoothed_vals, label=f"{agent} (smoothed)", color=color, linewidth=2.0)

        ax.set_title(title, fontsize=12, fontweight='bold', pad=8)
        ax.set_xlabel("Steps", fontsize=10)
        ax.set_ylabel(ylabel, fontsize=10)
        ax.grid(True, linestyle="--", alpha=0.6)
        if has_data:
            ax.legend(loc="best", frameon=True)

    fig.suptitle(f"{run_id} Training Performance Dashboard", fontsize=16, fontweight='bold', y=0.98)
    plt.tight_layout()

    summary_path = os.path.join(base_dir, f"{run_id}_summary.png")
    plt.savefig(summary_path, dpi=200, bbox_inches='tight')
    plt.close()
    print(f" 종합 요약 대시보드 저장 완료: {summary_path}")

    # 2. 개별 고화질 그래프 저장
    individual_targets = [
        ("Environment/Cumulative Reward", "cumulative_reward.png", "Cumulative Reward", "Reward"),
        ("Environment/Episode Length", "episode_length.png", "Episode Length", "Steps"),
        ("Losses/Policy Loss", "policy_loss.png", "Policy Loss", "Loss"),
        ("Losses/Value Loss", "value_loss.png", "Value Loss", "Loss"),
        ("Policy/Entropy", "entropy.png", "Policy Entropy", "Entropy"),
        ("Policy/Extrinsic Value Estimate", "value_estimate.png", "Value Estimate", "Estimate")
    ]

    for tag, filename, title, ylabel in individual_targets:
        plt.figure(figsize=(9, 5))
        has_data = False
        for agent, metrics in data.items():
            if tag in metrics and metrics[tag]["steps"]:
                has_data = True
                steps = metrics[tag]["steps"]
                raw_vals = metrics[tag]["values"]
                color = colors.get(agent, "#2ca02c")
                plt.plot(steps, raw_vals, color=color, alpha=0.25)
                smoothed_vals = smooth(raw_vals, weight=0.8)
                plt.plot(steps, smoothed_vals, label=f"{agent} (smoothed)", color=color, linewidth=2.0)

        if has_data:
            plt.title(f"{title} ({run_id})", fontsize=13, fontweight='bold')
            plt.xlabel("Steps", fontsize=11)
            plt.ylabel(ylabel, fontsize=11)
            plt.grid(True, linestyle="--", alpha=0.6)
            plt.legend(loc="best")
            out_file = os.path.join(out_dir, filename)
            plt.tight_layout()
            plt.savefig(out_file, dpi=200)
            plt.close()

    print(f" 개별 그래프 저장 완료: {out_dir}")

    # 3. 콘솔 요약 출력
    print("\n--- [학습 최종 요약] ---")
    for agent, metrics in data.items():
        if "Environment/Cumulative Reward" in metrics:
            vals = metrics["Environment/Cumulative Reward"]["values"]
            steps = metrics["Environment/Cumulative Reward"]["steps"]
            if vals:
                final_10_avg = sum(vals[-10:]) / len(vals[-10:])
                max_reward = max(vals)
                print(f" [{agent}] 총 스텝: {steps[-1]:,} | 최종 평균 보상(최근 10회): {final_10_avg:.3f} | 최대 보상: {max_reward:.3f}")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="ML-Agents Run Graph Exporter")
    parser.add_argument("run_id", nargs="?", default="test_02", help="결과 폴더명 (예: test_02)")
    args = parser.parse_args()
    export_run_graphs(args.run_id)
