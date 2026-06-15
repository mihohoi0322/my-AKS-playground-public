# WordPress + MySQL シークレット管理ガイド

このディレクトリでは、次の 2 パターンを扱います。

1. Kubernetes Secret（学習用途）
2. Azure Key Vault Provider for Secrets Store CSI Driver + Workload Identity

## ファイル一覧
| ファイル | 役割 |
|----------|------|
| `secret.yaml` | 平文(stringData) で DB/WordPress 用の Secret を定義（学習用途） |
| `deployment.yaml` | Secret を参照する MySQL / WordPress 構成 |
| `keyvault-secretproviderclass.yaml` | Workload Identity 用 `SecretProviderClass` |
| `deployment-keyvault.yaml` | `ServiceAccount` + Key Vault CSI 検証用 Pod |

---
## 1. Kubernetes Secret 方式

```bash
kubectl apply -f 08_Secret/secret.yaml
kubectl apply -f 08_Secret/deployment.yaml
kubectl get pods -l app=wordpress -w
kubectl get svc wordpress-web
```

動作確認:
```bash
POD=$(kubectl get pod -l app=wordpress,tier=frontend -o jsonpath='{.items[0].metadata.name}')
kubectl exec $POD -- env | grep -E '^WORDPRESS_DB_(HOST|NAME|USER)='
kubectl exec $POD -- sh -c 'test -n "$WORDPRESS_DB_PASSWORD" && echo wordpress-db-password-env-present'
kubectl get secret wordpress-db-secret
```

---
## 2. Key Vault CSI + Workload Identity 方式（Microsoft Learn 準拠）

公式:
- https://learn.microsoft.com/azure/aks/csi-secrets-store-driver
- https://learn.microsoft.com/azure/aks/csi-secrets-store-identity-access

### 前提
- Azure CLI で対象サブスクリプションにサインイン済み
- `kubectl` の current context が対象 AKS クラスターを向いている
- User Assigned Managed Identity と Key Vault を作成できる Azure RBAC 権限がある
- Key Vault 名はグローバル一意にする必要がある

```bash
RG=<your-resource-group>
AKS_NAME=<your-aks-name>
LOCATION=<your-location>

SUFFIX=$(openssl rand -hex 3)
UAMI=uami-kv-csi-hol-$SUFFIX
KEYVAULT_NAME=kvhol$SUFFIX
SECRET_NAME=ExampleSecret
SECRET_VALUE='Hello from Key Vault'
SERVICE_ACCOUNT_NAMESPACE=$(kubectl config view --minify --output 'jsonpath={..namespace}')
SERVICE_ACCOUNT_NAMESPACE=${SERVICE_ACCOUNT_NAMESPACE:-default}
SERVICE_ACCOUNT_NAME=workload-identity-sa
FEDERATED_IDENTITY_NAME=aks-keyvault-csi

az aks update -g $RG -n $AKS_NAME --enable-oidc-issuer --enable-workload-identity
az aks enable-addons -g $RG -n $AKS_NAME --addons azure-keyvault-secrets-provider

# CSI Driver / Azure Provider Pod が kube-system にいることを確認
kubectl get pods -n kube-system -l 'app in (secrets-store-csi-driver,secrets-store-provider-azure)' -o wide

az identity create -g $RG -n $UAMI --location $LOCATION

USER_ASSIGNED_CLIENT_ID=$(az identity show -g $RG -n $UAMI --query clientId -o tsv)
IDENTITY_PRINCIPAL_ID=$(az identity show -g $RG -n $UAMI --query principalId -o tsv)
IDENTITY_TENANT=$(az aks show -g $RG -n $AKS_NAME --query identity.tenantId -o tsv)
AKS_OIDC_ISSUER=$(az aks show -g $RG -n $AKS_NAME --query oidcIssuerProfile.issuerUrl -o tsv)

az keyvault create -g $RG -n $KEYVAULT_NAME -l $LOCATION --enable-rbac-authorization
KEYVAULT_SCOPE=$(az keyvault show -n $KEYVAULT_NAME --query id -o tsv)

# シークレット作成権限を現在のユーザーに付与してから、検証用 Secret を作成
CALLER_OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
az role assignment create \
	--role "Key Vault Secrets Officer" \
	--assignee-object-id $CALLER_OBJECT_ID \
	--assignee-principal-type User \
	--scope $KEYVAULT_SCOPE

az keyvault secret set \
	--vault-name $KEYVAULT_NAME \
	--name $SECRET_NAME \
	--value "$SECRET_VALUE"

# secret 参照なら Key Vault Secrets User を付与
az role assignment create \
	--role "Key Vault Secrets User" \
	--assignee-object-id $IDENTITY_PRINCIPAL_ID \
	--assignee-principal-type ServicePrincipal \
	--scope $KEYVAULT_SCOPE

# ServiceAccount と UAMI を federated credential で関連付け
az identity federated-credential create \
	--name $FEDERATED_IDENTITY_NAME \
	--identity-name $UAMI \
	--resource-group $RG \
	--issuer $AKS_OIDC_ISSUER \
	--subject system:serviceaccount:${SERVICE_ACCOUNT_NAMESPACE}:${SERVICE_ACCOUNT_NAME} \
	--audience api://AzureADTokenExchange
```

> Azure RBAC のロール割り当て反映には数分かかることがあります。Pod の mount が失敗した場合は、少し待ってから Pod を再作成してください。

### マニフェスト編集
`08_Secret/keyvault-secretproviderclass.yaml` と `08_Secret/deployment-keyvault.yaml` には以下の placeholder があります:
- `<USER_ASSIGNED_CLIENT_ID>`
- `<YOUR_KEYVAULT_NAME>`
- `<YOUR_TENANT_ID>`
- `<YOUR_SECRET_NAME>`

リポジトリ内の YAML は placeholder のまま残し、適用用ファイルを一時ディレクトリに生成します:

```bash
TMP_MANIFEST_DIR=$(mktemp -d "$TMPDIR/learn-k8s-08.XXXXXX")

sed \
	-e "s/<USER_ASSIGNED_CLIENT_ID>/$USER_ASSIGNED_CLIENT_ID/g" \
	-e "s/<YOUR_KEYVAULT_NAME>/$KEYVAULT_NAME/g" \
	-e "s/<YOUR_TENANT_ID>/$IDENTITY_TENANT/g" \
	-e "s/<YOUR_SECRET_NAME>/$SECRET_NAME/g" \
	08_Secret/keyvault-secretproviderclass.yaml > "$TMP_MANIFEST_DIR/keyvault-secretproviderclass.yaml"

sed \
	-e "s/<USER_ASSIGNED_CLIENT_ID>/$USER_ASSIGNED_CLIENT_ID/g" \
	08_Secret/deployment-keyvault.yaml > "$TMP_MANIFEST_DIR/deployment-keyvault.yaml"
```

### デプロイ
```bash
kubectl apply -f "$TMP_MANIFEST_DIR/keyvault-secretproviderclass.yaml"
kubectl apply -f "$TMP_MANIFEST_DIR/deployment-keyvault.yaml"
kubectl wait --for=condition=Ready pod/sc-demo-keyvault-csi --timeout=240s
```

### 動作確認
```bash
kubectl exec sc-demo-keyvault-csi -- ls -la /mnt/secrets-store
kubectl exec sc-demo-keyvault-csi -- sh -c "test -s /mnt/secrets-store/$SECRET_NAME && echo secret-file-present"
kubectl get secret keyvault-synced-secret -o go-template='{{.metadata.name}} type={{.type}} dataKeys={{range $k, $v := .data}}{{$k}} {{end}}'
```
