window.FESTIVAL_DATA = {

  settings: {
    title: "読祭 マッチングゲーム！",
    cardBack: "images/card-back.png",
    cardsPerGame: null,
    hintAfterMistakes: 3
  },

  departments: [
    { id: "eizo-housou", name: "映像クリエイター学科・放送映像学科",   boothName: "EIZO / BROADCAST",   icon: "📷", image: "images/departments/eizo-housou.png", color: "#E0286E" },
    { id: "tv",          name: "TVディレクター学科",                   boothName: "TV DIRECTOR",        icon: "🎬", image: "images/departments/tv.png",          color: "#C8283C" },
    { id: "it-team",     name: "ITエンジニア学科・ITキャリア養成科",   boothName: "IT ENGINEER / CAREER", icon: "💻", image: "images/departments/it-team.png",   color: "#1F78E8" },
    { id: "arch-team",   name: "建築学科・SD研究会・建築研究会",       boothName: "ARCHITECTURE",       icon: "🏢", image: "images/departments/arch-team.png",   color: "#9A6232" },
    { id: "denki",       name: "電気電子学科",                         boothName: "ELECTRIC",           icon: "⚡", image: "images/departments/denki.png",       color: "#F2B800" },
    { id: "rinsho",      name: "臨床工学科",                           boothName: "CLINICAL ENG.",      icon: "❤️", image: "images/departments/rinsho.png",      color: "#E04A52" },
    { id: "kaigo",       name: "介護福祉学科",                         boothName: "CARE & WELFARE",     icon: "🤲", image: "images/departments/kaigo.png",       color: "#D4508C" }
  ],


  foods: [
    { id: "churros",        name: "チュロス",               image: "images/foods/churros.png",         emoji: "🥖", departmentId: "eizo-housou" },
    { id: "chocobanana",    name: "チョコバナナ",           image: "images/foods/chocobanana.png",     emoji: "🍌", departmentId: "it-team" },

    { id: "icecream",       name: "アイスクリーム",         image: "images/foods/icecream.png",        emoji: "🍦", departmentId: "eizo-housou" },
    { id: "yakisoba",       name: "焼きそば",               image: "images/foods/yakisoba.png",        emoji: "🍜", departmentId: "eizo-housou" },

    { id: "yakitori",       name: "焼き鳥",                 image: "images/foods/yakitori.png",        emoji: "🍢", departmentId: "kaigo" },
    { id: "molkky",         name: "モルック",               image: "images/foods/molkky.png",          emoji: "🎯", departmentId: "it-team" },
    { id: "sui-gyoza",      name: "水餃子",                 image: "images/foods/sui-gyoza.png",       emoji: "🥟", departmentId: "tv" },

    { id: "minigame",       name: "ミニゲーム",             image: "images/foods/minigame.png",        emoji: "🎮", departmentId: "it-team" },
    { id: "escape",         name: "脱出ゲーム",             image: "images/foods/escape.png",          emoji: "🚪", departmentId: "eizo-housou" },

    { id: "ai-newspaper",   name: "AIで自分新聞",           image: "images/foods/ai-newspaper.png",    emoji: "📰", departmentId: "it-team" },
    { id: "toast",          name: "トースト",               image: "images/foods/toast.png",           emoji: "🍞", departmentId: "rinsho" },

    { id: "machi-sketch",   name: "まちと本棚スケッチ",     image: "images/foods/machi-sketch.png",    emoji: "✏️", departmentId: "arch-team" },
    { id: "tenshin",        name: "点心",                   image: "images/foods/tenshin.png",         emoji: "🥟", departmentId: "it-team" },

    { id: "chashitsu",      name: "茶室",                   image: "images/foods/chashitsu.png",       emoji: "🍵", departmentId: "arch-team" },
    { id: "gyoza",          name: "餃子",                   image: "images/foods/gyoza.png",           emoji: "🥟", departmentId: "kaigo" },

    { id: "matcha",         name: "抹茶",                   image: "images/foods/matcha.png",          emoji: "🍵", departmentId: "arch-team" },
    { id: "myanmar-tea",    name: "ミャンマーのお茶",       image: "images/foods/myanmar-tea.png",     emoji: "☕", departmentId: "arch-team" },
    { id: "myanmar-sweets", name: "ミャンマーのスイーツ",   image: "images/foods/myanmar-sweets.png",  emoji: "🍰", departmentId: "arch-team" },

    { id: "electric-work",  name: "電気工事",               image: "images/foods/electric-work.png",   emoji: "🔌", departmentId: "denki" },
    { id: "irairabo",       name: "イライラ棒",             image: "images/foods/irairabo.png",        emoji: "⚡", departmentId: "denki" },

    { id: "medical-exhibit",name: "医療機器展示・体験",     image: "images/foods/medical-exhibit.png", emoji: "🩺", departmentId: "rinsho" },
    { id: "yaki-gyoza",     name: "焼き餃子",               image: "images/foods/yaki-gyoza.png",      emoji: "🥟", departmentId: "tv" },

    { id: "elderly-sim",    name: "高齢者疑似体験",         image: "images/foods/elderly-sim.png",     emoji: "🧓", departmentId: "kaigo" },
    { id: "teyoku",         name: "手浴体験",               image: "images/foods/teyoku.png",          emoji: "🤲", departmentId: "kaigo" },
    { id: "fukushi-yogu",   name: "福祉用具展示",           image: "images/foods/fukushi-yogu.png",    emoji: "♿", departmentId: "kaigo" }
  ],

  messages: {
    instruction: "カードを、やっている学科の屋台へ！（ドラッグ、またはカードをタップ → 屋台をタップ）",
    progressLabel: "せいかい Correctly placed:",
    selected: "「{food}」をえらんだよ。どの屋台かな？",
    pickCardFirst: "先にカードをえらんでね",
    boothFull: "{dept}の屋台はもうかんせい！ ✓ ほかの屋台にためしてね",
    correct: [
      "せいかい！ {food}は{dept}だよ！",
      "ナイス！ {dept}の{food}！",
      "ぴったり！ {food}は{dept}で体験できるよ！"
    ],
    wrong: [
      "ちがうよ、もう一度！ Try again!",
      "おしい！ もう一度ためしてね。 Try again!",
      "うーん、どこの屋台かな？ Try again!"
    ],
    hint: "ヒント：ピカピカ光っている屋台を見てみて！",
    dropHere: "ここに置く",
    allPlaced: "🎉",
    congratsTitle: "ぜんぶ せいかい！ おめでとう！",
    congratsText: "Congratulations! すべての屋台が正しいカードでそろったよ！",
    congratsHint: "タップでとじる",
    allDone: "🎉 ぜんぶ せいかい！ おめでとう！ 🎉"
  }
};