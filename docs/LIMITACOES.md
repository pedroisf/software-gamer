# Limites de validação da versão 0.1.0

Esta é uma primeira versão funcional para validação, não uma certificação de produção. Não promete ganho de FPS, aceitação por anti-cheat nem ausência de regressões em hardware não testado.

- GPN, roteamento, servidores, driver próprio, assinatura EV e multipath foram removidos do escopo.
- Interface/recuperação são processos do usuário. Não há serviço Windows permanente, elevação nem acesso a perfis de outros usuários.
- Limpeza de caches de browsers/launchers, shaders e temporários do Windows é delegada às ferramentas oficiais, sem automação destrutiva pelo app.
- Game Mode e Capturas abrem configurações do Windows; alterações feitas lá são manuais e não fazem parte da restauração automática.
- Não há tweaks de CPU/PCIe/Registro/TCP, limpeza de RAM, Prefetch, pagefile, encerramento forçado de apps ou injeção em jogos.
- Recuperação no reboot ocorre no próximo logon e depende de o executável permanecer no caminho registrado. Não funciona antes do logon ou se todos os processos de recuperação forem impedidos de executar.
- Comparar o valor atual ao aplicado não detecta uma mudança externa A→B→A. Por isso a aplicação registra conflitos observáveis e evita ajustes globais amplos.
- Assinatura comercial e homologação em jogos/anti-cheats reais não estão incluídas na automação de CI. Nenhuma proteção de segurança deve ser desativada.
- O runner Windows do GitHub valida APIs, arquivos de teste e inicialização/renderização WPF; isso não substitui testes em Windows 11 físico, notebooks, alta escala/DPI, Narrator ou uma bateria longa de sessões reais.
- Updater autenticado automático, medição FPS/frametime e testes térmicos não foram implementados. Distribuição é manual por Releases, com checksums; checksums não substituem assinatura de fabricante.

Revisão técnica e testes físicos são gates antes de chamar esta versão de produto final homologado. Consulte `docs/VALIDACAO.md` para evidências e pendências.
