# Guia do usuário

## Instalação

Windows 11 x64. Baixe `SoftwareGamer-Setup-x64.exe` nos Releases do repositório e execute normalmente, sem elevar para administrador. O runtime .NET está incluído. A instalação é por usuário e cria uma entrada no menu Iniciar.

Alternativamente use o executável portátil `SoftwareGamer.exe`. Mantenha-o num local estável, fora de diretórios temporários. Não mova/remova o executável enquanto houver ajustes ativos: o caminho é usado pela recuperação no logon. Verifique o SHA-256 com `Get-FileHash .\SoftwareGamer.exe -Algorithm SHA256` e compare ao arquivo da mesma release.

A versão inicial não tem assinatura digital comercial. Não desative antivírus, Secure Boot, HVCI ou anti-cheat. Se a política do computador bloquear a execução, não contorne o bloqueio; use somente uma distribuição autorizada pelo administrador.

## Limpeza

1. Abra Limpeza e selecione Analisar.
2. Revise os arquivos e marque os que deseja remover da pasta temporária.
3. Mova os selecionados para quarentena. Isso preserva conteúdo e não libera espaço.
4. Na aba Recuperação, restaure itens ou exclua permanentemente o item selecionado. A exclusão exige confirmação e não pode ser desfeita.

A análise inclui apenas `.tmp` e `.temp` antigos (7 dias), no Temp local do usuário, em NTFS. Arquivos em uso, links, hardlinks, somente leitura, arquivos alterados e destinos não autorizados são ignorados. Cada plano expira após 10 minutos. A quarentena tem limite de 2 GiB e não é purgada automaticamente. Não execute a limpeza enquanto joga; jogos cadastrados em execução bloqueiam a operação.

Se o original tiver sido recriado, a restauração não o sobrescreve. Preserve os dois arquivos e resolva o conflito manualmente. O app não conta movimentação para quarentena como espaço liberado; o total exibido após purga é tamanho lógico, não medição de blocos físicos.

## Perfis

Adicione o executável real do jogo, escolha um nome e salve. Nenhum ajuste vem marcado. Opcionalmente escolha AboveNormal e/ou um plano de energia existente. Os planos não são criados nem alterados internamente; apenas selecionados durante a sessão, quando na tomada.

Inicie o jogo, atualize a lista de processos e aplique ao executável correspondente. Você pode autorizar aplicação automática enquanto o app estiver aberto. Cada executável é identificado por caminho e SHA-256; após atualização do jogo, recadastre o perfil. Launchers e processos auxiliares não são incluídos implicitamente. Jogos protegidos podem negar acesso; o app não contorna essa restrição.

Somente um perfil com ajustes ativos por vez. Ao encerrar o jogo, fechar a janela ou passar para bateria, os ajustes aplicáveis são restaurados. Um processo auxiliar de recuperação observa falhas da GUI. Trocas de plano também registram uma entrada temporária `SoftwareGamerRecovery` no Run do usuário, removida após recuperação; ela tenta restaurar no próximo logon após reinício abrupto, não antes do logon.

## Recuperação e desinstalação

“Restaurar todos os ajustes” pausa a automação na sessão atual. Mudanças externas geram conflito e são preservadas. “Manter estado atual do conflito” encerra a tentativa de restauração sem sobrescrever a configuração atual. A recuperação não altera configurações que você mudou manualmente pelas telas do Windows.

Antes de desinstalar, restaure/encerre ajustes e restaure ou purgue os arquivos em quarentena. O desinstalador bloqueia a remoção enquanto houver pendências. Dados do usuário em `%LOCALAPPDATA%\SoftwareGamer` são preservados; não apague `state.json` enquanto houver pendências. Se ele estiver corrompido, o app bloqueia alterações, em vez de presumir valores padrão.

## Privacidade e atualizações

Sem conta, anúncios, telemetria, API de IA ou servidor de backend. O diagnóstico exportado não contém caminhos pessoais nem conteúdo de arquivos. Abrir o repositório utiliza o navegador. Atualizações são manuais pelo GitHub Releases; não há atualizador automático nesta versão.
