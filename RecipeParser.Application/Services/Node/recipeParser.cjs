module.exports = {
    parseIngredientLine: async (line, lang = 'en', options = {}) => {
        const mod = await import('@jlucaspains/sharp-recipe-parser');
        return { ...mod.parseIngredient(line, lang, options), fullIngredient: line }
    },
    parseInstructionLine: async (line, options = {}) => {
        const mod = await import('@jlucaspains/sharp-recipe-parser');
        return { ...mod.parseInstruction(line, options), fullInstruction: line }
    }
};