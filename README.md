### 概要
liltoonをもとにアウトラインのみを描画するシェーダーを作成し、元アバターPrefabからシェーダーやFXを置き換えたPrefabのコピーを作成するconverter。

趣味で作成したので利用は自己責任。

### 使い方
1. 変換元のアバターPrefabをシーン上に配置（liltoonを導入していることが前提）。
2. 配置したPrefabを右クリックし、OutlineConverter→アウトライン用Prefabを作成をクリック。
3. GenerateフォルダにPrefabが作成するので、それをシーン上に配置。元のPrefabは削除してよい。
4. 目など、メッシュ以外で表現される部位も描画したい場合、Photoshopなどで描画したいアウトラインだけを白塗りしたマスクを作成し、顔のmaterialに設定。
5. アップロードして表示を確認。

### ダウンロード
Download latest release:
https://github.com/miuye256/Lzebul_Outline/releases/latest
