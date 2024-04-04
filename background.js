// background.js

// Função para manipular mensagens recebidas da extensão
chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    // Verifica se a mensagem recebida é do tipo 'getGameData'
    if (request.action === 'getGameData') {
        // Chama a função para obter os dados do jogo
        const gameData = findGame();
        
        // Envia os dados do jogo de volta para a extensão
        sendResponse(gameData);
    }
});

// Função para encontrar os jogos com estrela
function findGame() {
    // Seleciona todos os elementos que representam jogos
    const jogos = document.querySelectorAll('.event__match');
    
    // Percorre cada jogo para verificar se tem estrela
    for (const jogo of jogos) {
        const estrelaPresente = jogo.querySelector('.eventSubscriber__star--active');

        if (estrelaPresente) {
            // Se a estrela estiver presente, obtenha o ID do jogo e outras informações
            const jogoId = jogo.id;
            const nomeJogadorHome = jogo.querySelector('.event__participant--home').textContent;
            const nomeJogadorAway = jogo.querySelector('.event__participant--away').textContent;
            const placarHome = jogo.querySelector('.event__score--home').textContent;
            const placarAway = jogo.querySelector('.event__score--away').textContent;
            const resultadoParte1Home = jogo.querySelector('.event__part--home.event__part--1').textContent;
            const resultadoParte1Away = jogo.querySelector('.event__part--away.event__part--1').textContent;
            const resultadoParte6Home = jogo.querySelector('.event__part--home.event__part--6').textContent;
            const resultadoParte6Away = jogo.querySelector('.event__part--away.event__part--6').textContent;
            
            // Retorna os dados do jogo
            return { 
                jogoId, 
                nomeJogadorHome, 
                nomeJogadorAway, 
                placarHome, 
                placarAway, 
                resultadoParte1Home, 
                resultadoParte1Away, 
                resultadoParte6Home, 
                resultadoParte6Away
            };
        }
    }

    // Retorna null se nenhum jogo com estrela for encontrado
    return null;
}
