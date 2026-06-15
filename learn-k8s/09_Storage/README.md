# WordPress + Azure Files CSI (NFS) サンプル

このフォルダは WordPress の `/var/www/html` を Azure Files で利用するサンプルです。

## 事前条件
0. 変数設定
```bash
RG=<RESOURCE_GROUP>
AKS_NAME=<AKS_CLUSTER>
```
1. AKS クラスター (Kubernetes バージョンがサポート範囲)
2. Azure Files CSI Driver を有効化する

```bash
az aks update -g $RG -n $AKS_NAME --enable-file-driver
kubectl get storageclass
```

このサンプルは Azure Files NFS を使います。Microsoft Learn の推奨に合わせて `StorageClass` では `protocol: nfs` と `skuName: PremiumV2_LRS` を指定しています。NFS 共有は Premium 系 SKU と 100Gi 以上の PVC が前提です。利用中の AKS / Azure Files CSI Driver が `PremiumV2_LRS` に未対応の場合は、`09_Storage/storageclass.yaml` の `skuName` を `Premium_LRS` に変更してください。


## 適用手順 (動的プロビジョニング版)
以下のコマンドは `learn-k8s` ディレクトリから実行します。

```bash
kubectl apply -f 09_Storage/storageclass.yaml

# PVC (PV は StorageClass により自動作成)
kubectl apply -f 09_Storage/pvc.yaml
kubectl wait --for=jsonpath='{.status.phase}'=Bound pvc/wordpress-pvc --timeout=600s
kubectl get pv,pvc

# Secret + Deployments + Services
kubectl apply -f 09_Storage/deployment.yaml
kubectl rollout status deploy/mysql --timeout=300s
kubectl rollout status deploy/wordpress --timeout=300s
kubectl get pods -l app=wordpress -o wide
kubectl get pods -l app=mysql -o wide

# PVC マウント確認
POD=$(kubectl get pod -l app=wordpress -o jsonpath='{.items[0].metadata.name}')
kubectl exec "$POD" -- mount | grep /var/www/html || true
kubectl exec "$POD" -- sh -c 'test -d /var/www/html/wp-content && echo wp-content-directory-present'

# WordPress の外部 IP 確認
kubectl get svc wordpress -o wide
```

WordPress は `wordpress-storage-db-secret` から `WORDPRESS_DB_*` を、MySQL は同じ Secret から `MYSQL_*` を読み込みます。学習用に `stringData` へダミー値を書いていますが、本番では Key Vault CSI や External Secrets Operator などへ置き換えてください。
