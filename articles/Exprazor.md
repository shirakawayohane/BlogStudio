---
title: "VirtualDOMの作り方"
created_at: "2021-12-21"
updated_at: "2022-01-26"
categories:
    - C#
    - Web
---

# はじめに
Advent Calendar初参加です。
仮想DOMによるUIフレームワークは、特にJS界ではReactやVue、hyperappなど多くの選択肢がある中、C#だと、現状Blazorぐらいしか無いのではないでしょうか。（あったら失礼しました！）

そんな中、C#でも様々な選択肢を取れるようになればと思い、今回はC#による仮想DOMのUIフレームワークを作ってみました。

この記事ではまず、今回作ってみたフレームワークであるExprazorのAPIや実装を紹介し、内部実装のうち仮想DOMの仕組みを中心に解説していきたいと思います。

:::note warn
いくつか簡単なサンプルケースで動作を確認していますが、今の所動作確認は不十分です。
:::
:::note warn
後述するTypescriptとの相互運用など、まだ実装が完了していない機能があります。
:::
:::note info
パッケージ公開はまだしていません。試してみたい方は.NET6動作環境でリポジトリをクローンしてお試しいただけます。
:::
https://github.com/WiZLite/Exprazor

# 今回作ったもの
C#で実装した(SSRの[^ssr])仮想DOMのUIフレームワークです。
[![Image from Gyazo](https://i.gyazo.com/ca004b16dca43af7b8fc9c38347d1179.gif)](https://gyazo.com/ca004b16dca43af7b8fc9c38347d1179)

[![Image from Gyazo](https://i.gyazo.com/1c4cd06069800ab49070fd95641730e4.gif)](https://gyazo.com/1c4cd06069800ab49070fd95641730e4)

[^ssr]: サーバーでは無くライブラリとして使うポテンシャルはありますが、現状SSRの実装しかないので、SSRとしました。
端的に換言すると[Blazor](https://docs.microsoft.com/ja-jp/aspnet/core/blazor/?view=aspnetcore-6.0)です。
しかし、せっかく自分で作るので、Blazorに対して以下の点で差別化をしています。

1. 式としてDOM構造を扱う
2. WebにおけるシームレスなTypescriptとの相互運用
3. Web以外のプラットフォーム（Unity等）でも動く設計を重視する

では実際に使い心地などを紹介していきます。

## 1. 式としてDOM構造を扱う[^0]
特殊なテンプレート記法などを用いずに、以下のようなRender関数によって「式として」Viewを表現します。

[^0]: Exprazorの名前の由来はここから来ています。

```cs
protected override IExprazorNode Render(CounterState state)
{
    return
    Elm("div", new() { ["id"] = "counter" },
        Elm("div", null,
            Text("Counter")
        ),
        Text(state.Value.ToString()),
        Elm("button", new() { ["onclick"] = () => SetState(state with { Value = state.Value + 1 }) },
            Text("+")
        )
    );
}
```

Viewを式として定義出来ることには以下のようなメリットがあると考えています。

- テンプレートの文法などを覚える必要がない
  - テンプレートに対する学習コストがかからない
  - 開発環境の開発をする必要がない

- ユーザーコードとフレームワーク自体のコードが近い
  - 裏でAttributeを用いて依存性やプロパティを注入をしたりと、フレームワークの挙動として把握する必要のある知識が減る

- 表現力を損なわない
  - C#なので今後入ってくるであろう文法への対応などは特に必要ない
  - 早期リターンや小さな単位の使いまわしなどが可能になる

表現力に関しては良くも悪くもかもしれませんが、[^1]テンプレートの書き心地を諦めることによって得られるものは、思ったよりも多い気がします。

[^1]: もちろん、完璧に動く便利なものがベストです。しかしBlazorは今日でこそアナライザも安定してきてホットリロードも効き快適な環境になりましたが、そこにたどり着くまでには多くの開発者とPreviewユーザーの労力を要したはずです

## 2. Webにおける[^2]Typescriptとのシームレスな相互運用
[^2]: 一応Unityでも動かすつもりがあるのであえて「Webにおける」と書いています

これは単純にやってみたかった！
やりたいことはこれです。

```cs:Splitter.cs
//略

ElementReference left;
ElementReference right;
protected override IExprazorNode Render(SplitState state) {
    return
    Elm("div", new() { ["style"] = "{... 幅、flexの設定など}"},
        Elm("div", new() { ["@ref"] = _ref => left = _ref; }, Text("Upper")),
        Elm("div", new() { ["@ref"] = _ref => right = _ref; }, Text("Lower"))
    );
}
protected override void OnAfterRender() {
    Split(a, b);
}
```

```ts:Splitter.ts(同ディレクトリ)
import split from "split.js"
// 名前空間は分離される
function Split(a : HTMLElement, b : HTMLElement) {
    split(a,b); // jsのライブラリを呼ぶ
}
```
端的に換言すると、同ディレクトリにTSを書くと関数のバインディングを自動生成する機能です。
Blazorを使っていた頃から、こんなのがあったらいいんじゃないかな...と考えていましたが、現実的に考えるとなかなか難しそうだし、MSのフレームワークとして、Typescriptというもう一つの言語が採用されることは無いだろう、と思っていました。
しかし、自分で作るとなったら話は別。やるんです。
実際、C#ではSourceGeneratorという武器も揃ってきて、.NET6では[Incremental Source Generator](https://zenn.dev/pcysl5edgo/articles/6d9be0dd99c008)と、言語機能として（重要）コンパイル時に動的なコードを生成する技術は発達してきているので、もうそろそろMS以外の個人でも出来る頃合ではないでしょうか。（出来ると思っているので実装中です。）

実際、開発中の進捗ではIncremental Source Generatorを使っているのですが、[スギノコさん](https://twitter.com/pCYSl5EDgo)のこの記事を参考にさせていただいています！知見が少ない中大変ありがたいです。

https://zenn.dev/pcysl5edgo/articles/6d9be0dd99c008

実はこの記事の時点でこの機能も実装してドヤっ！と出そうと思っていたのですが、昨日の夜の時点でこんなことを言ってたあたりからお察しください。（でき次第更新させていただきます）


https://twitter.com/wizlightyear/status/1472646230023225344?s=20

https://twitter.com/wizlightyear/status/1472821459915767809?s=20


## 3.Web以外のプラットフォーム（Unity等）でも動く設計を重視する
前例だと、Reactに対するReact Nativeなどがありますね。C#なので、それをUnity上でやってみたら良いのでは、と考えたりしています。

若干内部実装に差し掛かりますが、Exprazorのコア部分では、仮想DOMを比較した際に以下のようなコマンドをバッファリングし、これを消費するイベントハンドラの口を提供することで、実際にWebSocket等でブラウザに送りつけて動作しています。

```cs:DOMCommands.cs
    using Id = System.Int32;
    [MessagePackFormatter(typeof(DOMCommandFormatter))]
    public interface DOMCommand {}
    public record struct SetStringAttribute(Id Id, string Key, string Value) : DOMCommand;
    public record struct SetNumberAttribute(Id Id, string Key, double Value) : DOMCommand;
    public record struct SetBooleanAttribute(Id Id, string Key, bool Value) : DOMCommand;
    public record struct SetTextNodeValue(Id Id, string Text) : DOMCommand;
    public record struct RemoveAttribute(Id Id, string Key) : DOMCommand;
    public record struct CreateTextNode(Id Id, string Text) : DOMCommand;
    public record struct CreateElement(Id Id, string Tag) : DOMCommand;
    public record struct AppendChild(Id ParentId, Id NewId) : DOMCommand;
    public record struct InsertBefore(Id ParentId, Id NewId, Id BeforeId) : DOMCommand;
    public record struct RemoveChild(Id ParentId, Id ChildId) : DOMCommand;
    public record struct RemoveCallback(Id Id, string Key) : DOMCommand;
    public record struct SetVoidCallback(Id Id, string Key) : DOMCommand;
    public record struct SetStringCallback(Id Id, string Key) : DOMCommand;
```

確かにWeb向けにしか見えないコマンド群ですが、逆に考えてみると
**同様の粒度を持ち、構造的に同等の操作が出来るオペレーション群**をクライアント側に用意することが出来れば、あとは普通にライブラリとして参照するなり、プロセス間通信をしたり、TCPで通信するなりしてコマンドを送受信して、ExprazorのAPIによってUIを構築出来るようになるはずです。
Exprazorでは、以上のような野心的な機能を実装出来るように、Webで有ることを関知しすぎず、なるべく疎結合になるようにする、という方針を心がけています。

ちなみにUnityだと、UIElementsあたりが良いのかなぁ、と検討をつけていますが、詳しさが足りないので追々の課題にさせていただきます。

# 使い方
さて、~~ポジショントークはこの辺りにして~~実際の使い方を簡潔に紹介しようと思ったのですが、README.ja.md で自分が書いた事とほとんど被ってしまうと思うので、こちらに譲りたいと思います。


https://github.com/WiZLite/Exprazor/blob/master/README.ja.MD#%E5%88%9D%E3%82%81%E3%81%A6%E3%81%AE%E3%82%B3%E3%83%B3%E3%83%9D%E3%83%BC%E3%83%8D%E3%83%B3%E3%83%88

# 仮想DOMの仕組み

参考：差分検知のコアロジック

https://github.com/WiZLite/Exprazor/blob/master/src/Exprazor/ExprazorCore.cs

仮想DOMでは、以下のような、実際のUI木構造に対応したオブジェクトや、それを一定の単位で分割するためのコンポーネントを用意します。

```cs
public interface IExprazorNode 
{
    int NodeId { get; }
}

// 実際にはロジックが生えていたりします。
public record TextNode(int NodeId, string Text) : IExprazorNode;

public record HTMLNode(int NodeId, string Tag, Dictionary<string, object?>? Attributes, IEnumerable<IExprazorNode>? Children) : IExprazorNode;

// かなり省略していますが、要は、DOMを出力することが出来る状態付きの関数みたいなものです。
public abstract class Component : IExprazorNode : IExprazorNode
{
    public abstract IExprazorNode Render();
}
```
これらを組み立てて、実際のDOM上で作りたい構造に対応する構造を構築する手段を用意します。

DOMの構築手段として、Componentに以下のようなメソッドを生やしています。実質コンストラクタのラッパーです。

```cs
protected IExprazorNode Elm(string tag, Attributes? attributes, IEnumerable<IExprazorNode>? children) => new HTMLNode(Context, tag, attributes, children);

protected IExprazorNode Elm(string tag, Attributes? attributes, params IExprazorNode[] children) => new HTMLNode(Context, tag, attributes, children);

protected IExprazorNode Elm<TComponent>(object props) where TComponent : Component, new()
{
    var ret = new TComponent
    {
        Context = Context,
        Props = props,
        ParentId = this.ParentId,
    };
    return ret;
}
protected IExprazorNode Text(string text) => new TextNode(text);
```

フレームワークは、この木構造が更新される度に、更新内容の差分を、なるべく最小限になるように検知します。
初回のレンダリングでは、「全てのノードが無から作り出される」という差分が検知されるでしょう。

実際に、以下のようなVDOMを初回に計算した場合、画像のような差分として検知されます。

```cs
Elm("div", new() { ["id"] = "counter" },
    Elm("div", null,
        Text("Counter")
    ),
    Text(state.Value.ToString()),
    Elm("button", new() { ["onclick"] = () => SetState(state with { Value = state.Value + 1 }) },
        Text("+")
    )
);
```

![image.png](https://qiita-image-store.s3.ap-northeast-1.amazonaws.com/0/289197/70a57ebe-49de-6804-340c-46dba4c54dc9.png)

button エレメントのAttributeの "onclick" には関数が設定されています。

```cs
["onclick"] = () => SetState(state with { Value = state.Value + 1 })
```
SetStateにより自身の状態をValueを１増やして更新する関数ですね。
ボタンを押した場合、Valueを参照しているこの部分が実際に更新されることが期待されます。

```cs
Text(state.Value.ToString())
```

実際にボタンが押されると、この関数が呼ばれるのですが（このあたりはフレームワークが管理）
SetStateで状態が更新されると、コンポーネントのRenderが呼ばれ、
その結果であるVDOMと古いVDOMが比較されることになります。

VDOMを再帰的に比較するPatch関数によって、各Attributeの部分やテキストの内容、及びそれらを再帰的に比較します。
今回の場合、カウンター表示部分以外の差分は無いので、ほとんどの部分では差分が検知されず素通りしますが、以下の部分のコードで、差分が検知されます。

<details><summary>Patch関数全体</summary><div>

```cs:ExprazorCore.cs
static internal void Patch(ExprazorApp context, Id parentId, Id nodeId, IExprazorNode? oldVNode, IExprazorNode newVNode, in List<DOMCommand> commands)
{
    newVNode.NodeId = nodeId;

    if (oldVNode == newVNode) return;

    if(oldVNode == null)
    {
        var createdId = CreateNode(context, newVNode, commands);
        commands.Add(new AppendChild(parentId, createdId));

        return;
    }
    
    if(oldVNode.GetType() != newVNode.GetType())
    {
        var createdId = CreateNode(context, newVNode, commands);
        commands.Add(new InsertBefore(parentId, createdId, nodeId));
        commands.Add(new RemoveChild(parentId, nodeId));
        oldVNode.Dispose();

        return;
    }

    if (oldVNode is TextNode oldTextNode && newVNode is TextNode newTextNode)
    {
        if (oldTextNode.Text != newTextNode.Text)
        {
            commands.Add(new SetTextNodeValue(newTextNode.NodeId, newTextNode.Text));
        }
    }
    else if (oldVNode is Component oldComponent && newVNode is Component newComponent)
    {
        if (!oldComponent.Props.Equals(newComponent.Props))
        {
            var newState = newComponent.PropsChanged(newComponent.Props);
            var newTree = newComponent.Render(newState);
            Patch(context, newComponent.ParentId, newComponent.NodeId, newComponent.lastTree, newTree, commands);
            newComponent.lastTree = newTree;
        }
        else
        {
            newComponent.State = oldComponent.State;
            newComponent.lastTree = oldComponent.lastTree;
        }
    }
    else if (oldVNode is HTMLNode oldHTMLNode && newVNode is HTMLNode newHTMLNode)
    {
        if (oldHTMLNode.Tag != newHTMLNode.Tag)
        {
            var createId = CreateNode(context, newVNode, commands);
            commands.Add(new InsertBefore(parentId, createId, nodeId));
            commands.Add(new RemoveChild(parentId, oldVNode.NodeId));
            oldVNode.Dispose();
        }
        else
        {
            foreach (var key in (oldHTMLNode.Attributes?.Keys ?? Enumerable.Empty<string>())
            .Union(newHTMLNode.Attributes?.Keys ?? Enumerable.Empty<string>()))
            {
                object? oldValue = null;
                object? newValue = null;
                oldHTMLNode.Attributes?.TryGetValue(key, out oldValue);
                newHTMLNode.Attributes?.TryGetValue(key, out newValue);
                if (oldValue != newValue)
                {
                    PatchAttribute(context, nodeId, key, oldValue, newValue, commands);
                }
            }
        }

        LinkedList<IExprazorNode> oldChildren = new LinkedList<IExprazorNode>(oldHTMLNode.Children ?? Enumerable.Empty<IExprazorNode>());
        LinkedList<IExprazorNode> newChildren = new LinkedList<IExprazorNode>(newHTMLNode.Children ?? Enumerable.Empty<IExprazorNode>());
        // STEP 0:
        // Patch same key nodes from both side.
        // A B C d ... x Y Z    =>  d ... x
        // A B C D ... X Y Z        D ... X
        while(oldChildren.Any() && newChildren.Any())
        {
            var oldChild = oldChildren.First!.Value;
            var newChild = newChildren.First!.Value;
            var oldKey = oldChild.GetKey();
            if (oldKey == null || oldKey != newChild.GetKey()) break;
            Patch(context, nodeId, oldChild.NodeId, oldChild, newChild, commands);
            oldChildren.RemoveFirst();
            newChildren.RemoveFirst();
        }
        while (oldChildren.Any() && newChildren.Any())
        {
            var oldChild = oldChildren.First!.Value;
            var newChild = newChildren.First!.Value;
            var oldKey = oldChild.GetKey();
            if (oldKey == null || oldKey != newChild.GetKey()) break;
            Patch(context, nodeId, oldChild.NodeId, oldChild, newChild, commands);
            oldChildren.RemoveLast();
            newChildren.RemoveLast();
        }
        // STEP 1:
        // old: A B C D E       => A B X...C D E
        // new: A B X...C D E 
        //          ↑ insert new node if vnode has inserted.
        if(!oldChildren.Any())
        {
            while(newChildren.Any())
            {
                var createdId = CreateNode(context, newChildren.First!.Value, commands);
                commands.Add(new InsertBefore(nodeId, createdId, oldVNode.NodeId));
                newChildren.RemoveFirst();
            }
        // STEP 2:
        // old: A B C D E => A B E
        // new: A B     E
        //          ↑ remove node if vnode has removed
        } else if(!newChildren.Any())
        {
            while(oldChildren.Any())
            {
                var nodeToRemove = oldChildren.First!.Value;
                commands.Add(new RemoveChild(nodeId, nodeToRemove.NodeId));
                nodeToRemove.Dispose();
                oldChildren.RemoveFirst();
            }
        } else
        {
            var keyed = oldChildren.Where(x => x.GetKey() != null).ToDictionary(x => x.GetKey()!, x => x);
            // loop until all of the newChildren is patched.
            while (newChildren.Any())
            {
                var oldChild = oldChildren.First();
                var newChild = newChildren.First();
                var oldKey = oldChild.GetKey();
                var newKey = newChild.GetKey();
                var nextKey = oldChildren.First?.Next?.Value?.GetKey();

                // N is null. x and X are different.

                // STEP 3:
                // old : N x y z ...    =>    x y z ...
                // new : X Y Z ...   (Remove) X Y Z ...
                if (newKey != null && newKey.Equals(nextKey) && oldKey == null)
                {
                    commands.Add(new RemoveChild(nodeId, oldChild.NodeId));
                    oldChild.Dispose();
                    oldChildren.RemoveFirst();
                    newChildren.RemoveFirst();
                    continue;
                }

                // STEP 4:
                // if both null, patch and go next.
                // old : N x y...    =>     x y...
                // new : N X Y...  (Patch)  X Y...
                if(newKey == null && oldKey == null)
                {
                    Patch(context, nodeId, oldChild.NodeId, oldChild, newChild, commands);
                    oldChildren.RemoveFirst();
                    newChildren.RemoveFirst();
                    continue;
                }
                // STEP 5:
                // If newKey is null, find similar node from old, if exists, patch with that, else create new node.
                // old : x y N...  =>   N x y...    =>     x y...
                // new : N X Y...       N X Y...  (Patch)  X Y...
                if(newKey == null && oldKey != null)
                {
                    var type = newChild.GetType();
                    var patchTarget = oldChildren.FirstOrDefault(x => x.GetKey() == null && x.GetType() == type);
                    if(patchTarget != null)
                    {
                        commands.Add(new InsertBefore(nodeId, patchTarget.NodeId, oldChild.NodeId));
                        Patch(context, nodeId, patchTarget.NodeId, patchTarget, newChild, commands);
                        oldChildren.Remove(patchTarget);
                        newChildren.RemoveFirst();
                        continue;
                    } else
                    {
                        CreateNode(context, newChild, commands);
                        newChildren.RemoveFirst();
                        continue;
                    }
                }
                // STEP 6:
                // if both are same, Just patch and proceed.
                // old : A y z...  => 
                // new : A Y Z... (Patch)
                if(oldKey != null && oldKey.Equals(newKey))
                {
                    Patch(context, nodeId, oldChild.NodeId, oldChild, newChild, commands);
                    oldChildren.RemoveFirst();
                    newChildren.RemoveFirst();
                    continue;
                }
                // STEP 7:
                // If old keys contains current newKey, insert it into current head and patch.
                //      ---------  (A will be skipped from next time.)
                //      ↓       ↑
                // old : x y... A    =>    A x y...    =>     x y...
                // new : A X Y...  (Sort)  A X Y...  (Patch)  X Y...
                if(keyed.TryGetValue(newKey!, out var oldChildWithSameKey))
                {
                    oldChildren.Remove(oldChildWithSameKey);
                    commands.Add(new InsertBefore(nodeId, oldChildWithSameKey.NodeId, oldChild.NodeId));
                    Patch(context, nodeId, oldChildWithSameKey.NodeId, oldChildWithSameKey, newChild, commands);
                    newChildren.RemoveFirst();
                    continue;
                }
                // STEP 8:
                // if old keys don't contians 'X' and old one is null, create X node and proceed.
                // old : ...    =>            ... 
                // new : X Y...   (Create X)  Y...
                Patch(context, nodeId, oldChild.NodeId, null, newChild, commands);
                newChildren.RemoveFirst();
            }

            // STEP 9:
            // If oldchildren still left, remove them.
            while(oldChildren.Any())
            {
                var oldChild = oldChildren.First!.Value;
                commands.Add(new RemoveChild(nodeId, oldChild.NodeId));
                oldChild.Dispose();
                oldChildren.RemoveFirst();
            }
        }

    }
}
```

</div></details>

```cs:ExprazorCore.csのPatch関数の中身
        if (oldVNode is TextNode oldTextNode && newVNode is TextNode newTextNode)
        {
            if (oldTextNode.Text != newTextNode.Text)
            {
                commands.Add(new SetTextNodeValue(newTextNode.NodeId, newTextNode.Text));
            }
        }
```
このコードパスを通ることによって、 `SetTextNodeValue`コマンドが、commandsのリストに追加されます。
差分比較を一通り終えると、ブラウザには以下のコマンドが届き、カウンタが更新されます。

![image.png](https://qiita-image-store.s3.ap-northeast-1.amazonaws.com/0/289197/4d053f80-d1a7-a1ce-2ded-12a6b41aa688.png)

[![Image from Gyazo](https://i.gyazo.com/0ff6c13a8c1980581bd64dcc3473266c.gif)](https://gyazo.com/0ff6c13a8c1980581bd64dcc3473266c)

アトリビュートの比較は、基本的にキー名の増減と、一致するキーの値の差分を検知します。（全部載せ）

```cs:ExprazorCore.cs
static void PatchAttribute(ExprazorApp context, Id nodeId, string key, object? oldValue, object? newValue, in List<DOMCommand> commands)
{
    if (key == "key") { }
    else if (key.StartsWith("on"))
    {
        if (newValue == null)
        {
            commands.Add(new RemoveCallback(nodeId, key));
        }
        else if (newValue is Action newAct && Object.ReferenceEquals(newValue, oldValue) == false)
        {
            context.AddOrSetCallback(nodeId, key, newAct);
            commands.Add(new SetVoidCallback(nodeId, key));
        }
        else if (newValue is Action<string> stringAct && Object.ReferenceEquals(newValue, oldValue) == false)
        {
            context.AddOrSetCallback(nodeId, key, stringAct);
            commands.Add(new SetStringCallback(nodeId, key));
        }
    }
    else
    {
        if (newValue == null)
        {
            commands.Add(new RemoveAttribute(nodeId, key));
        }
        else if (oldValue == null || !newValue.Equals(oldValue))
        {
            if (newValue is byte or sbyte or short or ushort or int or long or ulong or ulong or float or double or decimal)
            {
                commands.Add(new SetNumberAttribute(nodeId, key, (double)newValue));
            }
            else if (newValue is string str)
            {
                commands.Add(new SetStringAttribute(nodeId, key, str));
            }
            else if (newValue is bool b)
            {
                commands.Add(new SetBooleanAttribute(nodeId, key, b));
            }
        }
    }
}
```

改めてまとめると、状態が更新される度に、ツリーの

- アトリビュート
- テキストの内容
- 子要素の増減を比較

を比較し、子要素に対しても再帰的に[^4]比較しながら、差分を検知[^3]していきます。
[^3]: JSだったらそのまま反映してしまえそうですが、別言語なので、コマンドオブジェクトとして貯めています
[^4]: 子要素の増減を比較する際、ユーザーが指定するkeyという値を頼りにして効率化を図ったりするのですが、これがまた中々奥深い気がします。

以上をユーザーからの入力に対して継続的に行っていくことにより、インタラクティブなUIが完成します。

# まとめ
いかがでしたでしょうか。フレームワーク作りはかなり大変ですが、非常に学びがありました。アロケーションは全く気にしていなかったので、パフォーマンスを気にし始めると大変そうです。

まだ開発中ですので、問題点でも良いので反響をいただけると大変励みになります。コントリビュートも大歓迎です。

今回は、一つの記事に対して開発を力みすぎましたが、この開発から派生する、Source Generatorに関する知見なども今後共有出来ればと思っています。

では、よいクリスマスを！

# さいごに
GitHubスターをください！

https://github.com/WiZLite/Exprazor
