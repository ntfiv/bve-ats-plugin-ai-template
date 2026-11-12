# Third-party notices

このリポジトリ自体は MIT License([LICENSE](LICENSE))です。次の第三者のソフトウェアを含む、または
ビルド時に取得して使います。

## DllExport(`DllExport.bat` を同梱 / パッケージはビルド時に NuGet から取得)

- 用途: C# のメソッドをネイティブ DLL のエクスポートにする(ビルド時に IL を書き換える)。
- 配布元: https://github.com/3F/DllExport (NuGet id: `DllExport`)
- Copyright (c) 2009-2015 Robert Giesecke
- Copyright (c) 2016-2025 Denis Kuzmin <x-3F@outlook.com> github/3F
- ライセンス: MIT License(https://github.com/3F/DllExport の LICENSE を参照)

DllExport のパッケージ(`packages/` 以下)は、このリポジトリには含めていません(`.gitignore` で除外)。
ビルド時に `DllExport.bat -action Restore` が NuGet から取得します。パッケージには、`ildasm` / `ilasm`
(.NET の `coreclr` ツール)や Mono.Cecil など、さらに別の第三者のソフトウェアが含まれます。
そのライセンスは、取得したパッケージ内の `3rd-party-NOTICES.txt` と `tools\coreclr\LICENSE.TXT` を参照してください。

## ATS Plugin Multiplexer(同梱していません・参照のみ)

- https://github.com/magicant/bve-plugin-multiplexer
- このリポジトリは Multiplexer のコードを含みません。[docs/KNOWHOW.md](docs/KNOWHOW.md) は、そのソースコードを
  読んで分かった呼び出しの挙動を、自分の言葉で説明しています。

## DetailManager(同梱していません・参照のみ)

- https://github.com/mikangogo/DetailManager (Unlicense)
- このリポジトリは DetailManager のコードを含みません。[docs/KNOWHOW.md](docs/KNOWHOW.md) は、そのソースコードを
  読んで分かった呼び出しの挙動を、自分の言葉で説明しています。

## NAudio など(同梱していません・参照のみ)

[docs/KNOWHOW.md](docs/KNOWHOW.md) に、NAudio のような外部パッケージに依存するプラグインを作る場合の注意点を書いています。
**このテンプレート自体はそれらに依存しません。**

プラグインに外部パッケージ(NAudio、NAudio.Vorbis、NVorbis など)を足したら、次のようにしてください
(`AGENTS.md` の手順 5 にも書いてあります):

1. この節を、**実際に使うパッケージ**の一覧(名前・版・ライセンス・配布元)に書き換える。「依存しません」の文は消す。
2. 各パッケージのライセンスを配布元で確認し、必要な著作権表示・ライセンス文を、配布物に含める。
   ライセンス文は、取得したパッケージの中(`%USERPROFILE%\.nuget\packages\<パッケージ名>\<版>\` の `LICENSE` / `license.txt` など)か、
   配布元のリポジトリにあります。パッケージによっては、含まれていないこともあります。
