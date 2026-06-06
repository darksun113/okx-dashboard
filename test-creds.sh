#!/bin/bash
# 隔离测试 OKX 凭据 —— 不把密钥写进命令历史，终端也不会做智能引号替换。
# 用法： bash test-creds.sh
set -euo pipefail

read -rp "API Key: " APIKEY
read -rsp "Secret Key: " SECRET; echo
read -rsp "Passphrase: " PASS; echo
read -rp "模拟盘账户? (y/N): " DEMO

# 显示 passphrase 的字节，帮助发现隐藏/特殊字符（弯引号、全角、尾随空格等）
echo
echo "Passphrase 字符数: ${#PASS}"
echo "Passphrase 十六进制 (每个字节):"
printf '%s' "$PASS" | xxd
echo

TS=$(python3 -c "import datetime;print(datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%S.')+f'{datetime.datetime.now().microsecond//1000:03d}Z')")
PATH_REQ="/api/v5/account/balance"
PREHASH="${TS}GET${PATH_REQ}"
SIGN=$(printf '%s' "$PREHASH" | openssl dgst -sha256 -hmac "$SECRET" -binary | base64)

HDR_DEMO=()
if [[ "$DEMO" == "y" || "$DEMO" == "Y" ]]; then
  HDR_DEMO=(-H "x-simulated-trading: 1")
fi

echo "==> 调用 OKX ${PATH_REQ}"
curl -s "https://www.okx.com${PATH_REQ}" \
  -H "OK-ACCESS-KEY: ${APIKEY}" \
  -H "OK-ACCESS-SIGN: ${SIGN}" \
  -H "OK-ACCESS-TIMESTAMP: ${TS}" \
  -H "OK-ACCESS-PASSPHRASE: ${PASS}" \
  -H "Content-Type: application/json" \
  "${HDR_DEMO[@]}"
echo
