# Como gerar o jogo (Windows)

O jogo distribuído é uma pasta com o `Guilty.exe` e, dentro dela, `Backend/`:
o backend Python (FastAPI + IA) empacotado como executável. O jogo inicia e
encerra o backend sozinho — o jogador não instala Python nem abre servidor.

Os dois repositórios precisam estar lado a lado:

```
Guilty/
├── agent-orchestrator-api/   ← backend (Python)
└── Guilty-Game-Tcc/          ← este projeto (Unity)
```

## 1. Empacotar o backend

Sempre que o backend mudar. Na pasta `agent-orchestrator-api`:

```bash
venv\Scripts\python.exe scripts\empacotar_backend.py
```

Gera `dist/guilty-backend/` (~83 MB) e já testa o executável. O código-fonte
continua sendo a fonte da verdade: o executável é só um produto gerado, e
pode ser refeito a qualquer momento.

## 2. Gerar o jogo

No Unity: menu **Guilty > Build - Gerar jogo (Windows)**.

Ou sem abrir o editor (com o Unity **fechado**), na pasta deste projeto:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe" -batchmode -quit -projectPath . -executeMethod GuiltyBuild.BuildWindows -logFile build.log
```

Resultado:

- `Builds/Windows/` — o jogo pronto para rodar (`Guilty.exe`)
- `Builds/Guilty-Windows-v<versão>.zip` — o que vai para o GitHub Releases

O build falha logo no início, com o comando do passo 1 na mensagem, se o
backend ainda não tiver sido empacotado.

## 3. Publicar

Suba o `.zip` em **GitHub > Releases > Draft a new release**. Nunca commite
`Builds/` (está no `.gitignore`). A versão do zip vem de
*Project Settings > Player > Version*.

## O que o jogador precisa

- Windows 10/11 64 bits.
- **Gemini**: internet e uma chave gratuita do Google AI Studio, colada em
  *Configurações > Detetive (IA)*. Cada jogador usa a própria chave — nunca
  embuta uma chave do grupo no jogo.
- **Qwen 2.5 3B (local)**: 8 GB de RAM e um download de 2,1 GB feito pelo
  próprio jogo, em *Configurações > Detetive (IA)*. Depois roda offline.
  Respostas levam ~20-30 s em CPU.

Os modelos baixados e os logs ficam em
`%USERPROFILE%\AppData\LocalLow\DefaultCompany\Guilty\`.

## Desenvolvendo no editor

Dar Play já sobe o backend a partir do código-fonte
(`../agent-orchestrator-api`, via `venv`), lendo o `.env` de desenvolvimento.
Não é preciso empacotar nada nem subir servidor à mão. Se você já tiver um
servidor rodando na porta 8000, o jogo usa ele.

Para mudar a página *Detetive (IA)*: edite `Assets/Editor/GuiltyAiSettingsUI.cs`
e rode **Guilty > UI - Página Detetive (IA)**. Para conferir o visual sem dar
Play: **Guilty > UI - Screenshots Detetive (IA)** (saem em `PilotScreens/`).
