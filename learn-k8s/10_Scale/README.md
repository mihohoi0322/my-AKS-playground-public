# HPA スケールアウト検証サンプル

このフォルダは `autoscaling/v2` の HorizontalPodAutoscaler を使い、CPU とメモリ利用率に応じた Pod の水平スケールを確認するサンプルです。

## 事前条件
1. AKS クラスターで metrics-server が動作していること
2. 対象 Deployment の container に `resources.requests` が設定されていること

```bash
kubectl top nodes
```

`kubectl top nodes` が値を返さない場合は、metrics-server の起動直後でメトリクスがまだ集計されていない可能性があります。数分待ってから再確認してください。

## 適用手順
以下のコマンドは `learn-k8s` ディレクトリから実行します。

```bash
kubectl apply -f 10_Scale/deployment.yaml
kubectl rollout status deploy/hpa-demo --timeout=180s
kubectl get deploy hpa-demo -o wide
kubectl get hpa hpa-demo
kubectl get svc hpa-demo-svc -o wide
```

作成される主なリソースは次の通りです。

| リソース | 名前 | 内容 |
|---------|------|------|
| Deployment | `hpa-demo` | HPA 対象の nginx Pod。初期 replicas は 2 |
| Service | `hpa-demo-svc` | クラスター内から負荷をかけるための ClusterIP Service |
| HorizontalPodAutoscaler | `hpa-demo` | CPU 60% / メモリ 70% を目標に 2〜10 replicas でスケール |

## 負荷生成
軽い `wget` 1 ループだけでは CPU 使用率が HPA のしきい値まで上がらないことがあります。スケールアウトを観察する場合は、複数 Pod から並列にリクエストを送ります。

```bash
SERVICE_IP=$(kubectl get svc hpa-demo-svc -o jsonpath='{.spec.clusterIP}')

kubectl delete pod -l app=hpa-load --ignore-not-found

for i in 1 2 3 4; do
  kubectl run hpa-load-$i \
    --image=busybox:1.36 \
    --labels="app=hpa-load" \
    --restart=Never \
    -- /bin/sh -c "for worker in \$(seq 1 20); do while true; do wget -q -O- http://${SERVICE_IP}/ > /dev/null; done & done; wait"
done

kubectl get pods -l app=hpa-load
```

## スケールアウト確認
HPA は metrics-server の集計間隔に依存するため、反映まで数分かかることがあります。

```bash
kubectl get hpa hpa-demo -w
```

別ターミナルで Deployment の replicas も確認します。

```bash
kubectl get deploy hpa-demo -w
```

実行例では、CPU 使用率が HPA 目標値を超え、`hpa-demo` は最大 `10` replicas までスケールアウトしました。

## 負荷 Pod の削除
確認が終わったら、負荷生成 Pod を削除します。

```bash
kubectl delete pod -l app=hpa-load --ignore-not-found
kubectl get pod -l app=hpa-load
```

HPA の scaleDown には安定化時間があるため、負荷を止めても replicas がすぐに `minReplicas` へ戻らない場合があります。

## クリーンアップ
10 のリソースを削除する場合は次を実行します。

```bash
kubectl delete pod -l app=hpa-load --ignore-not-found
kubectl delete -f 10_Scale/deployment.yaml --ignore-not-found
```