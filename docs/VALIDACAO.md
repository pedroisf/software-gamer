# Validação da versão 0.1.1

## Verificação automatizada

- Build da aplicação WPF com .NET 10, warnings tratados como erros.
- Harness sem dependências externas: regras de elegibilidade, plano expirado, identidade alterada, journal inválido/schema futuro e concorrência de persistência.
- Integração Windows: análise, quarentena/restore/purga, arquivo ocupado/readonly, hardlink/symlink e troca de ancestral, limite de espaço, APIs de energia em leitura, identidade de processo e prioridade em filho de teste.
- Smoke da distribuição self-contained: inicialização WPF e renderização de cinco telas; a análise usa uma fixture artificial, nunca os temporários reais do runner.
- Instalador por usuário, distribuição portátil e hashes SHA-256.

Resultados e screenshots são anexados pelo workflow `Windows build and validation` como artefatos. A release identifica o commit correspondente. Consulte a execução vinculada à release para contagens finais.

Na execução [40012505822](https://github.com/pedroisf/software-gamer/actions/runs/40012505822), os **35 testes passaram, sem falhas e sem testes ignorados**. Cinco telas foram renderizadas e o instalador compilou. A revisão visual identificou contraste insuficiente e gerou uma correção posterior; a release usa uma execução mais recente, também vinculada na sua descrição.

## Pendências de homologação

O GitHub Actions usa Windows de servidor em seu runner e não comprova funcionamento em todas as builds do Windows 11. Ainda exigem teste físico: troca/restauração de plano OEM real, AC/DC, recuperação após queda de energia/reboot, watchdog sob falhas sistêmicas, instalação/remoção interativas, Narrator/alto contraste/DPI e compatibilidade com jogos e anti-cheats.

Nenhum resultado de FPS, frametime, temperatura ou ganho de desempenho foi medido nesta entrega. A primeira distribuição é uma pré-release para validação, não um produto certificado para uso irrestrito.

## Regressões da versão 0.1.1

A execução real do aplicativo (sem o caminho isolado `--smoke`) precisa abrir a janela com token administrativo herdado do runner. `startup/startup-result.json` registra janela visível, versão e contexto. O workflow falha se essa regressão não for exercitada.

A suíte inclui testes de comparação numérica de versões, canal de pré-release, rascunhos, instaladores ausentes/em envio, URLs fora do repositório, resposta inválida, HTTP sem credenciais, indisponibilidade/rate limit, cancelamento, tamanho de resposta e persistência da preferência. A consulta HTTP usa respostas simuladas; os testes não publicam versões fictícias.

O teste de instalador coloca o executável original 0.1.0 (hash fixado) no destino, instala a versão nova, verifica o hash instalado, abre a janela real e executa a desinstalação. O resultado fica em `installer-test/installer-result.json`. Isso cobre a regressão de upgrade sob token administrativo no runner, sem substituir homologação interativa em PCs físicos.
