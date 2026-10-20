# 強いSD-1 / さらに荒いRAT・遅延調整ガイド

## 今回の変更

ご希望に合わせ、**前版のRAT程度を今回のSD-1の基準**にしました。RATはそこからさらに深く歪ませ、中高域を別に歪ませる枝を混ぜてざらつきを加えています。実機SD-1/RATの回路を忠実に再現する目的ではなく、今回の好みに合わせた独自の音色です。

| 音色 | 初期値 | 変更点 |
| --- | --- | --- |
| SD-1 | Drive 64 / Tone 52 / Level 52 | 前版RATと同じゲイン・帯域・低域の厚みを土台に、少し非対称なクリップ |
| RAT | Distortion 75 / Filter 36 / Volume 52 | 高いゲイン、硬めのクリップ、歪ませた中高域の追加、明るめの音質 |
| AMP | Gain 8 / Bass・Middle・Treble 50 / Level 65 | ペダルの違いを残す比較設定 |

「SD-1 + AMP」「RAT + AMP」で同じフレーズを比較してください。最後にLevel/Volumeで近い音量にそろえて判断します。

## 遅延を減らす設定

前版のPC出力は60msを要求し、音声キューは最大100ms分を保持する設定でした。今回の初期値は次のとおりです。

- **PC出力のバッファ要求: 10ms**。⚙で10 / 20 / 40 / 60msを選べます。
- **音声キュー: 通常20msが上限**。最初から20ms待って再生する方式ではありません。
- ASIOの入力ブロックが大きい場合は、入力1ブロックとPC要求分を保持できる容量へ拡張します。1ブロックがキューに収まらず毎回切れることを避けます。
- 完全にOFFになった歪み／AMP段の重い計算を省くようにしました。8倍FIR処理は維持しています。

### まず行う手順

1. 新しいフォルダーへ展開し、`楽フェクター.sln`を開いて **Release / x64** で起動します。
2. ⚙で出力先を「PCのスピーカー」、PC出力バッファ要求を **10ms** にします。
3. 同じ設定画面の **「ASIO設定を開く（Buffer Size）」** を押します。
4. Yamaha Steinberg USB Driver側のASIO設定でBuffer Sizeをまず **128 samples** にします。安定している場合は64 samplesも比較できます。
5. 設定画面へ戻り、周波数はまず48000 Hzにして「適用・接続」を押します。
6. 弾いて確認します。音切れ・プチプチ音が出る場合はPC出力を20msにします。ASIOを64にしていた場合は128へ戻して比較します。

**10msはPC出力への要求値です。弾いてから聞こえるまでの合計が10msになる意味ではありません。** 入力ドライバー、実際のWindows出力バッファ、機器内部処理、音声キューなどが加わります。

画面下部に「ASIO 128 samples / PC要求10ms / キュー○ms」を表示します。長い場合は表示にマウスを置くと全文を読めます。キューの数値は今溜まっているアプリ内のデータで、実測の往復遅延ではありません。

演奏時の低遅延を優先するなら、出力先を「UR22CなどのASIO機器から出力」にしてUR22CのPHONES端子で聞く経路も比較してください。この経路はPCスピーカー用のWASAPI出力とキューを通りません。MIXはDAW側で確認します。PCスピーカー出力も引き続き選べます。

## 自分で音色を変える場所

まず `src/楽フェクター/AudioEffects/DriveVoicing.cs` を変更します。

| 変更する定数 | 現在値 | 変化 |
| --- | --- | --- |
| `Sd1.GainRange` | 95 | 大きくするとDriveを上げたときの歪みが強まる |
| `Sd1.PositiveCeiling` / `NegativeCeiling` | .48 / .44 | 小さくすると早くつぶれる。上下の差で非対称性が変わる |
| `Sd1.KneeWidth` | .055 | 大きくするとクリップへ移る角が柔らかくなる |
| `Sd1.CleanBlend` | .02 | 大きくすると原音成分を増やす |
| `Sd1.BodyDb` | 3.8 | 歪み後の低域の厚み |
| `Rat.GainRange` | 160 | 大きくすると、より弱い入力から深く歪む |
| `Rat.ClipCeiling` | .38 | 小さくすると、より早くつぶれる。出力音量も変わる |
| `Rat.KneeWidth` | .015 | 小さくすると角が硬くなる。0より大きくClipCeilingより小さくする |
| `Rat.GritMix` | .22 | **荒さを足す主要な値**。0で追加のざらつきなし、.30で強め |
| `Rat.GritGain` | 2.5 | 中高域だけの歪み枝のゲイン |
| `Rat.GritSplitHz` | 1400 | ざらつきを足す帯域の境界。位相も変わるため、単純な高域音量ノブではない |
| `Rat.InputHighPassHz` | 75 | 上げると歪み前の低域を締める。上げすぎると細くなる |
| `Rat.BodyDb` | 4.5 | 歪ませた後の低域の厚み |

たとえばRATをさらに荒くするには、最初は1項目だけ変更します。

```csharp
public const double GritMix = .30; // 元は.22。中高域のざらつきを増やす。
```

逆に、荒さを控えて深い歪みだけ残すなら.10にします。音が刺さるときはUIのFilterを上げて比較します。低音が膨らむときはBodyDbを下げるか、別の実験でInputHighPassHzを少し上げます。

初期ノブ値は `AudioEffects/ToneSettings.cs` に0～1で指定します。画面もこの値を参照するため、初期値はここだけで変更できます。変更後はアプリを止め、ビルドし直してください。

### その他のファイル

- `DriveCurves.cs`: クリップの形。
- `DriveStage.cs`: 入力の音質整形、歪み、低域整形、中高域の枝の混ぜ方。
- `AntialiasedRatClipper.cs`: RATの鋭いクリップで生じる折り返しを抑えるADAA処理。荒さの調整はまずGritMixやFilterで行います。
- `AmpStage.cs`: AMPの歪みと3バンドEQ。
- `src/CommonAudio/CommonAudioInput.cs`: PCへのWASAPI出力、ASIO入出力。
- `src/CommonAudio/AudioLatency.cs`: PC要求の初期値とキュー容量の計算。

## 比較音源

`comparison/` は同じ合成ギター風入力を使った設計確認用です。実際のギターや実機ペダルの録音ではありません。聞き比べ用にRMS音量をそろえているので、アプリ上の音量差そのままではありません。

- `00_previous_RAT_synthetic.wav`: 前版RAT。
- `01_clean_synthetic.wav`: 加工前の入力。
- `02_SD1_synthetic.wav`: 新SD-1 + AMP。
- `03_RAT_synthetic.wav`: 新RAT + AMP。
- `04_comparison_synthetic.wav`: **0～7秒が前版RAT、7.5～14.5秒が新SD-1、15～22秒が新RAT**。
- `dsp_comparison.png`: 今回のクリップ曲線、加工後の波形、倍音、低域の設計確認図。AMP OFFのテストです。

## 検証

- DSP/設定92項目、ASIO出力24項目、PC出力46項目、音色設計21項目が成功しました。
- 同じ500 Hz入力で、新SD-1のRMS音量は前版RATの約98.2%、波形差の正規化RMSは約2.0%。前版RATに近い基準へ移ったことを確認しました。
- 500 Hz入力の第5・7・9倍音の合計／基本波の振幅比は、前版RATが約.169、新RATが約.255。この条件で中高域の倍音が増えています。
- 9 kHz入力の折り返し成分は基本波に対して約-56.6 dB。8倍FIRとADAAを併用した、このテスト条件での結果です。
- 最適化ありのDSP検証では1000ブロック×256サンプルを約326msで処理し、定常処理の追加ヒープ割り当ては0でした。これは入出力遅延ではありません。
- 大きいASIOブロックの保持、PC出力停滞時に20msを超えて溜め続けないこと、旧設定から10ms要求への移行もテストしました。
- Windows上の実機再生と往復遅延の測定は、この環境では行えていません。設定値の削減と、実際の遅延が何ms減ったかは区別してください。

```powershell
dotnet run -c Release --project tests/SoundDemo.Checks/SoundDemo.Checks.csproj
dotnet run -c Release --project tests/PcOutput.Checks/PcOutput.Checks.csproj
dotnet run -c Release --project tests/ToneQuality.Checks/ToneQuality.Checks.csproj
```

参考:

- [UR22Cオペレーションマニュアル](https://download.steinberg.net/downloads_hardware/UR-C/Manuals/UR22C_Operation_Manual_Japanese.pdf)
- [Parker / Zavalishin / Le Bivic, DAFx 2016: 非線形処理のエイリアシング軽減](https://dafx.de/paper-archive/2016/dafxpapers/20-DAFx-16_paper_41-PN.pdf)
